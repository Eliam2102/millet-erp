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
public sealed class ExpedienteServicio(ComercioExteriorDbContext db, ICatalogosCeReadPort catalogos, ICurrentUserContext user, ICurrentUserPermissions permisos, IUsuarioSucursalReadPort sucursales, IOcImportacionReadPort compras, IClock clock)
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
        return await compras.ListarAsync(await SucursalesAsync(null, ct, escribir: true), q, ct);
    }

    public async Task<Dictionary<Guid, decimal>> CantidadesImportacionOtrosAsync(Expediente e, CancellationToken ct) =>
        await (from partida in db.Set<PartidaImportacion>()
            join contenedor in db.Contenedores on partida.ContenedorId equals contenedor.Id
            join expediente in db.Expedientes on partida.ExpedienteId equals expediente.Id
            where expediente.OcId == e.OcId && expediente.Id != e.Id && expediente.Estado != EstadoExpediente.Cancelado && contenedor.Estado == EstadoContenedor.Activo
            group partida by partida.OcLineaId into partidas
            select new { Linea = partidas.Key, Cantidad = partidas.Sum(p => p.Cantidad) }).ToDictionaryAsync(x => x.Linea, x => x.Cantidad, ct);

    public async Task<Expediente> CargarAsync(Guid id, bool escribir, CancellationToken ct)
    {
        var e = await db.Expedientes.Include(x => x.Incidencias).ThenInclude(i => i.Historial).Include(x => x.AvisosIncidencias).Include(x => x.Facturas).Include(x => x.AutorizacionesSalida).Include(x => x.Participantes).Include(x => x.Documentos).Include(x => x.Hitos).Include(x => x.Avisos).Include(x => x.CorreccionesHitos).Include(x => x.Contenedores).Include(x => x.PartidasImportacion).Include(x => x.Partidas).Include(x => x.VinculosPedidos).Include(x => x.Gastos).Include(x => x.PartidasCosto).Include(x => x.Distribuciones).Include(x => x.Anticipos).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new EntityNotFoundException("CE_EXPEDIENTE_NO_ENCONTRADO", "No existe el expediente.");
        await SucursalAsync(e.SucursalId, escribir, ct);
        return e;
    }

    public async Task<ExpedienteDetalle> DetalleAsync(Expediente e, CancellationToken ct)
    {
        var requisitos = await catalogos.RequisitosAsync(e.Tipo, ct);
        var adjuntos = await catalogos.AdjuntosVigentesAsync(e.Id, ct);
        var faltantes = e.Faltantes(requisitos, adjuntos);
        var hoy = clock.HoyLocal();
        var nombres = await catalogos.NombresAsync(e.Documentos.Select(d => d.ResponsableUsuarioId).Concat(e.Hitos.Select(h => h.ResponsableUsuarioId)).Concat(e.Incidencias.Select(i => i.ResponsableUsuarioId)).Concat(e.CorreccionesHitos.Select(c => c.UsuarioId)).Concat(new Guid?[] { e.CierreFisicoPorId, e.CierreDocumentalPorId, e.CierreFinancieroPorId, e.CierreAprobadoPorId }.OfType<Guid>()).Append(e.ResponsableUsuarioId).Distinct(), ct);
        var sucursal = await catalogos.SucursalesAsync([e.SucursalId], ct);
        var incoterm = await catalogos.IncotermsAsync([e.IncotermId], ct);
        var oc = e.OcId is Guid id ? await compras.ObtenerAsync(id, ct) : null;
        return new(Fila(e, nombres, sucursal, hoy), e.OcId, oc?.Folio, oc?.InformacionImportacion is { } info ? new ImportacionDto(info.IncotermId, info.PaisOrigen, info.NumeroContenedor, info.CodigoRuta, info.SemanaEmbarque, e.PedimentoVigente(info.NumeroPedimento)) : null, e.IncotermId, incoterm.GetValueOrDefault(e.IncotermId, "[INCOTERM]"), e.ResponsableUsuarioId, e.Tc, e.FechaTc, e.FuenteTc, e.MotivoCancelacion, e.RevisadoPorId, e.Participantes.Select(p => new ParticipanteDto(p.Id, p.Nombre, p.Tipo, p.Contacto, p.Papel, p.ProveedorId, p.ClienteId, p.Descripcion)).ToArray(), e.Documentos.OrderBy(d => d.TipoDocumentoId).ThenBy(d => d.NumeroVersion).Select(d => new DocumentoVersionDto(d.Id, d.TipoDocumentoId, d.NumeroVersion, d.AdjuntoId, d.ReemplazaA, d.Motivo, d.NoAplica, d.Estado, d.ResponsableUsuarioId, nombres.GetValueOrDefault(d.ResponsableUsuarioId, "[RESPONSABLE]"), d.CreatedAt, d.NumeroPedimento)).ToArray(), requisitos, faltantes, e.Incidencias.Any(i => i.Pendiente) ? "Con incidencias pendientes" : faltantes.Count > 0 ? "Incompleto" : e.RevisionPendiente ? "Revisión pendiente" : e.RevisadoPorId is null ? "Por revisar" : "Revisado", e.EstadoFisico(requisitos, adjuntos), e.EstadoFinanciero,
            e.Hitos.OrderBy(h => h.Orden).Select(h => new HitoDto(h.Id, h.Clave, HitoExpediente.Catalogo[h.Orden - 1].Nombre, h.Orden, h.FechaCompromiso, h.FechaReal, h.ResponsableUsuarioId, nombres.GetValueOrDefault(h.ResponsableUsuarioId, "[RESPONSABLE]"), h.EvidenciaAdjuntoId, h.Omitido, h.Motivo, h.MotivoCambioCompromiso, e.Estado == EstadoExpediente.Abierto && h.EstaAtrasado(hoy), e.Estado == EstadoExpediente.Abierto && h.EstaAtrasado(hoy) ? hoy.DayNumber - h.FechaCompromiso!.Value.DayNumber : 0, e.Incidencias.Any(i => i.Pendiente && i.HitoClave == h.Clave),
                e.CorreccionesHitos.Where(c => c.HitoId == h.Id).OrderByDescending(c => c.Fecha).Select(c => new CorreccionHitoDto(c.Id, c.FechaRealAnterior, c.OmitidoAnterior, c.FechaRealNueva, c.OmitidoNuevo, c.Motivo, c.UsuarioId, nombres.GetValueOrDefault(c.UsuarioId, "[USUARIO]"), c.Fecha)).ToArray())).ToArray(),
            e.Avisos.OrderByDescending(a => a.FechaAviso).ThenBy(a => a.Id).Select(a => new AvisoHitoDto(a.Id, a.HitoId, HitoExpediente.Catalogo[e.Hitos.Single(h => h.Id == a.HitoId).Orden - 1].Nombre, a.FechaCompromiso, a.FechaAviso, a.CerradoEn, a.MotivoCierre)).ToArray(),
            e.Incidencias.OrderByDescending(i => i.Fecha).Select(i => IncidenciaMap.Dto(i, nombres, hoy)).ToArray(),
            e.AvisosIncidencias.OrderByDescending(a => a.Fecha).Select(a => new AvisoIncidenciaDto(a.Id, a.IncidenciaId, a.DestinatarioUsuarioId, a.Fecha)).ToArray(), e.Facturas.Select(f => new FacturaExpedienteDto(f.Id, f.PedidoFacturableId, f.VersionEmbarque, f.Uuid, f.Folio, f.FechaTimbrado, f.Cancelada, f.UuidOriginal)).ToArray(), e.Cierres(requisitos, adjuntos, oc?.Partidas?.ToDictionary(p => p.Id, p => p.CantidadRecibida) ?? []).Select(c => c with { UsuarioNombre = c.UsuarioId is Guid u ? nombres.GetValueOrDefault(u, "Sin nombre") : null }).ToArray(), e.CierreAprobadoPorId, e.CierreAprobadoEn) { AduanaEntrada = e.AduanaEntrada };
    }

    public static ExpedienteFila Fila(Expediente e, IReadOnlyDictionary<Guid, string> nombres, IReadOnlyDictionary<Guid, string> sucursales, DateOnly hoy) => new(e.Id, e.Folio, e.Tipo, e.Referencia, e.SucursalId, sucursales.GetValueOrDefault(e.SucursalId, "[SUCURSAL]"), e.Estado, e.RevisionPendiente, e.Moneda, nombres.GetValueOrDefault(e.ResponsableUsuarioId, "[RESPONSABLE]"), e.Version, e.Estado == EstadoExpediente.Abierto ? e.Hitos.Count(h => h.EstaAtrasado(hoy)) : 0, e.Avisos.Count(a => a.Vigente));
}
