using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Incidencias;
using Millet.SharedKernel.Domain.Incidencias;

namespace Millet.Compartido.Application.Incidencias;

/// <summary>Permisos de incidencias (literales: Compartido no referencia Identidad; espejo de PermisosCanonicos).</summary>
public static class IncidenciasPermisos
{
    public const string Ver = "compartido.incidencias.ver";
    public const string Registrar = "compartido.incidencias.registrar";
    public const string Gestionar = "compartido.incidencias.gestionar";
    public const string LeerTodasSucursales = "compartido.incidencias.leer-todas-sucursales";
    public const string GestionarTodasSucursales = "compartido.incidencias.gestionar-todas-sucursales";
}

/// <summary>Nombres de usuario para el responsable y el actor (adaptador sobre Identidad en la Api).</summary>
public interface IIncidenciaUsuariosReadPort
{
    Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(IReadOnlyCollection<Guid> usuarioIds, CancellationToken cancellationToken);
}

/// <summary>
/// Reglas de acceso y alta de incidencias, compartidas por los handlers HTTP y el puerto <see cref="IIncidenciasPort"/>.
/// Alcance (ADR-0051): sin sucursal la ve quien tenga «ver»; con sucursal, quien esté asociado o tenga el bypass.
/// El responsable siempre ve y atiende las suyas.
/// </summary>
public sealed class IncidenciasServicio(
    CompartidoDbContext db,
    ICurrentUserContext usuario,
    ICurrentUserPermissions permisos,
    ICurrentEmpresaContext empresa,
    IUsuarioSucursalReadPort sucursales,
    IIncidenciaUsuariosReadPort usuarios,
    IClock clock)
{
    public Guid? UsuarioId => usuario.UserId;

    public Guid UsuarioRequerido =>
        usuario.UserId ?? throw new ForbiddenException("USUARIO_REQUERIDO", "No se pudo identificar al usuario.");

    public async Task<string> NombreUsuarioAsync(Guid id, CancellationToken ct)
    {
        var nombres = await usuarios.ObtenerNombresAsync([id], ct);
        return nombres.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : usuario.UserName ?? "Usuario";
    }

    public async Task<string> ResponsableValidoAsync(Guid responsableId, CancellationToken ct)
    {
        if (responsableId == Guid.Empty)
            throw new BusinessRuleException("INCIDENCIA_RESPONSABLE_INVALIDO", "Indique un responsable válido.");
        var nombres = await usuarios.ObtenerNombresAsync([responsableId], ct);
        return nombres.TryGetValue(responsableId, out var n) && !string.IsNullOrWhiteSpace(n)
            ? n
            : throw new BusinessRuleException("INCIDENCIA_RESPONSABLE_INVALIDO", "El responsable indicado no existe.");
    }

    /// <summary>Abre (o devuelve la pendiente igual). <paramref name="actorId"/> nulo = la abre el sistema.</summary>
    public async Task<(Incidencia Incidencia, bool YaExistia)> AbrirAsync(AbrirIncidenciaSolicitud s, Guid? actorId, CancellationToken ct)
    {
        var empresaId = empresa.Current
            ?? throw new BusinessRuleException("INCIDENCIA_EMPRESA_REQUERIDA", "No se pudo identificar la empresa de la incidencia.");
        var detalleClave = string.IsNullOrWhiteSpace(s.DetalleClave) ? string.Empty : s.DetalleClave.Trim();
        var paso = (s.Paso ?? string.Empty).Trim();
        var existente = await db.Incidencias.Include(x => x.Historial)
            .FirstOrDefaultAsync(x => x.Paso == paso && x.DocumentoId == s.DocumentoId && x.DetalleClave == detalleClave
                && (x.Estado == EstadoIncidencia.Abierta || x.Estado == EstadoIncidencia.EnAtencion), ct);
        if (existente is not null)
            return (existente, true);

        var responsable = await ResponsableValidoAsync(s.ResponsableUsuarioId, ct);
        string? actorNombre = actorId is Guid a ? await NombreUsuarioAsync(a, ct) : null;
        var incidencia = Incidencia.Abrir(new Incidencia.Datos(
            empresaId, s.Modulo, s.TipoDocumento, s.DocumentoId, s.DocumentoEtiqueta, s.SucursalId, paso, s.CodigoMotivo, s.Motivo,
            s.Detalle, detalleClave, s.ResponsableUsuarioId, responsable, s.FechaCompromiso, s.PasoReanudacion, s.RutaReanudacion),
            actorId, actorNombre, clock.UtcNow);
        db.Incidencias.Add(incidencia);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Carrera con otra alta igual: gana la que ya quedó (índice ux_incidencias_pendiente).
            if (await OtraPendienteAsync(paso, s.DocumentoId, detalleClave, ct) is not { } otra)
                throw;
            db.Entry(incidencia).State = EntityState.Detached;
            return (otra, true);
        }
        return (incidencia, false);
    }

    private async Task<Incidencia?> OtraPendienteAsync(string paso, Guid documentoId, string detalleClave, CancellationToken ct) =>
        await db.Incidencias.AsNoTracking().Include(x => x.Historial)
            .FirstOrDefaultAsync(x => x.Paso == paso && x.DocumentoId == documentoId && x.DetalleClave == detalleClave
                && (x.Estado == EstadoIncidencia.Abierta || x.Estado == EstadoIncidencia.EnAtencion), ct);

    public async Task<Incidencia> CargarAsync(Guid id, bool tracking, CancellationToken ct)
    {
        IQueryable<Incidencia> q = db.Incidencias.Include(x => x.Historial);
        if (!tracking) q = q.AsNoTracking();
        var i = await q.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException("INCIDENCIA_NO_ENCONTRADA", "No existe la incidencia solicitada.");
        await VerificarLecturaAsync(i, ct);
        return i;
    }

    public async Task VerificarLecturaAsync(Incidencia i, CancellationToken ct)
    {
        if (usuario.UserId is Guid u && u == i.ResponsableUsuarioId)
            return;
        if (!await permisos.TieneAsync(IncidenciasPermisos.Ver, ct))
            throw new ForbiddenException("INCIDENCIA_SIN_PERMISO", "No tienes permiso para consultar incidencias.");
        await VerificarSucursalAsync(i.SucursalId, IncidenciasPermisos.LeerTodasSucursales, ct);
    }

    public async Task VerificarSucursalAsync(Guid? sucursalId, string permisoBypass, CancellationToken ct)
    {
        if (sucursalId is not Guid s)
            return;
        if (await permisos.TieneAsync(permisoBypass, ct))
            return;
        if (usuario.UserId is Guid u && await sucursales.EstaAsociadoAsync(u, s, ct))
            return;
        throw new ForbiddenException("SUCURSAL_NO_ASOCIADA", "No tienes acceso a los datos de esta sucursal.");
    }

    /// <summary>Sucursales visibles para listar: <c>null</c> = todas (bypass); si no, las asociadas.</summary>
    public async Task<IReadOnlyList<Guid>?> SucursalesVisiblesAsync(CancellationToken ct)
    {
        if (await permisos.TieneAsync(IncidenciasPermisos.LeerTodasSucursales, ct))
            return null;
        return usuario.UserId is Guid u ? await sucursales.ListarIdsAsync(u, ct) : [];
    }

    public async Task<Acciones> AccionesAsync(Incidencia i, CancellationToken ct)
    {
        if (!i.Pendiente || usuario.UserId is not Guid u)
            return new Acciones(false, false, false, false);
        var responsable = u == i.ResponsableUsuarioId;
        var gestiona = await GestionaAsync(i, ct);
        var abrio = i.AbiertaPorId == u;
        var atiende = responsable || gestiona;
        return new Acciones(
            PuedeAtender: atiende && i.Estado == EstadoIncidencia.Abierta,
            PuedeResolver: atiende,
            PuedeCancelar: gestiona || abrio,
            PuedeReasignar: atiende);
    }

    private async Task<bool> GestionaAsync(Incidencia i, CancellationToken ct)
    {
        if (!await permisos.TieneAsync(IncidenciasPermisos.Gestionar, ct))
            return false;
        if (i.SucursalId is not Guid s || await permisos.TieneAsync(IncidenciasPermisos.GestionarTodasSucursales, ct))
            return true;
        return usuario.UserId is Guid u && await sucursales.EstaAsociadoAsync(u, s, ct);
    }

    public static void ExigirVersion(Incidencia i, int version)
    {
        if (i.Version != version)
            throw new ConcurrencyException("Incidencia", i.Id);
    }

    public static void Exigir(bool puede, string accion)
    {
        if (!puede)
            throw new ForbiddenException("INCIDENCIA_SIN_PERMISO", $"No puedes {accion} esta incidencia.");
    }

    public DateTimeOffset Ahora => clock.UtcNow;

    public async Task<IncidenciaDetalle> DetalleAsync(Incidencia i, CancellationToken ct) =>
        IncidenciaDetalle.De(i, await AccionesAsync(i, ct));
}

public sealed record Acciones(bool PuedeAtender, bool PuedeResolver, bool PuedeCancelar, bool PuedeReasignar);
