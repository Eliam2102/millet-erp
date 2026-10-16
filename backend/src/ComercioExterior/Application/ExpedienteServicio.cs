using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compras.Application.PublicPorts;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Identidad.Domain;

namespace Millet.ComercioExterior.Application;
/// <summary>Guardas compartidas por los casos de uso, incluidas las invocaciones fuera de HTTP.</summary>
public sealed class ExpedienteServicio(ComercioExteriorDbContext db, ICatalogosCeReadPort catalogos, ICurrentUserContext user, ICurrentUserPermissions permisos, IUsuarioSucursalReadPort sucursales, IOcImportacionReadPort compras)
{
    public Guid Actor => user.UserId ?? throw new UnauthorizedAccessException();

    public async Task PermisoAsync(string permiso, CancellationToken ct)
    {
        if (!await permisos.TieneAsync(permiso, ct))
            throw new ForbiddenException("CE_PERMISO_REQUERIDO", "No tiene permiso para esta acción.");
    }

    public Task SucursalAsync(Guid sucursal, bool escribir, CancellationToken ct) => SucursalScopeGuard.VerificarAsync(user.UserId, escribir ? PermisosCanonicos.ComercioExteriorDocumentosGestionarTodasSucursales : PermisosCanonicos.ComercioExteriorDocumentosLeerTodasSucursales, permisos, (uid, c) => sucursales.EstaAsociadoAsync(uid, sucursal, c), ct);
    public async Task<IReadOnlyList<Guid>?> SucursalesAsync(Guid? seleccion, CancellationToken ct, bool escribir = false)
    {
        if (seleccion is Guid id)
        {
            await SucursalAsync(id, escribir, ct);
            return[id];
        }

        if (await permisos.TieneAsync(escribir ? PermisosCanonicos.ComercioExteriorDocumentosGestionarTodasSucursales : PermisosCanonicos.ComercioExteriorDocumentosLeerTodasSucursales, ct))
            return null;
        return await sucursales.ListarIdsAsync(Actor, ct);
    }

    public async Task<IReadOnlyList<OcImportacionLectura>> OrdenesImportacionAsync(string? q, CancellationToken ct)
    {
        var ordenes = await compras.ListarAsync(await SucursalesAsync(null, ct, escribir: true), q, ct);
        var ids = ordenes.Select(x => x.Id).ToArray();
        var abiertas = await db.Expedientes.Where(x => x.OcId != null && ids.Contains(x.OcId.Value) && x.Estado == EstadoExpediente.Abierto).Select(x => x.OcId!.Value).ToListAsync(ct);
        return ordenes.Where(x => !abiertas.Contains(x.Id)).ToArray();
    }

    public async Task<Expediente> CargarAsync(Guid id, bool escribir, CancellationToken ct)
    {
        var e = await db.Expedientes.Include(x => x.Participantes).Include(x => x.Documentos).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new EntityNotFoundException("CE_EXPEDIENTE_NO_ENCONTRADO", "No existe el expediente.");
        await SucursalAsync(e.SucursalId, escribir, ct);
        return e;
    }

    public async Task<ExpedienteDetalle> DetalleAsync(Expediente e, CancellationToken ct)
    {
        var requisitos = await catalogos.RequisitosAsync(e.Tipo, ct);
        var faltantes = e.Faltantes(requisitos, await catalogos.AdjuntosVigentesAsync(e.Id, ct));
        var nombres = await catalogos.NombresAsync(e.Documentos.Select(d => d.ResponsableUsuarioId).Append(e.ResponsableUsuarioId).Distinct(), ct);
        var sucursal = await catalogos.SucursalesAsync([e.SucursalId], ct);
        var incoterm = await catalogos.IncotermsAsync([e.IncotermId], ct);
        var oc = e.OcId is Guid id ? await compras.ObtenerAsync(id, ct) : null;
        return new(Fila(e, nombres, sucursal), e.OcId, oc?.Folio, oc?.InformacionImportacion is { } info ? new ImportacionDto(info.IncotermId, info.PaisOrigen, info.NumeroContenedor, info.CodigoRuta, info.SemanaEmbarque, info.NumeroPedimento) : null, e.IncotermId, incoterm.GetValueOrDefault(e.IncotermId, "[INCOTERM]"), e.ResponsableUsuarioId, e.Tc, e.FechaTc, e.FuenteTc, e.MotivoCancelacion, e.RevisadoPorId, e.Participantes.Select(p => new ParticipanteDto(p.Id, p.Nombre, p.Tipo, p.Contacto, p.Papel, p.ProveedorId, p.ClienteId)).ToArray(), e.Documentos.OrderBy(d => d.TipoDocumentoId).ThenBy(d => d.NumeroVersion).Select(d => new DocumentoVersionDto(d.Id, d.TipoDocumentoId, d.NumeroVersion, d.AdjuntoId, d.ReemplazaA, d.Motivo, d.NoAplica, d.Estado, d.ResponsableUsuarioId, nombres.GetValueOrDefault(d.ResponsableUsuarioId, "[RESPONSABLE]"), d.CreatedAt)).ToArray(), requisitos, faltantes, faltantes.Count > 0 ? "Incompleto" : e.RevisionPendiente ? "Revisión pendiente" : e.RevisadoPorId is null ? "Por revisar" : "Revisado");
    }

    public static ExpedienteFila Fila(Expediente e, IReadOnlyDictionary<Guid, string> nombres, IReadOnlyDictionary<Guid, string> sucursales) => new(e.Id, e.Folio, e.Tipo, e.Referencia, e.SucursalId, sucursales.GetValueOrDefault(e.SucursalId, "[SUCURSAL]"), e.Estado, e.RevisionPendiente, e.Moneda, nombres.GetValueOrDefault(e.ResponsableUsuarioId, "[RESPONSABLE]"), e.Version);
}
