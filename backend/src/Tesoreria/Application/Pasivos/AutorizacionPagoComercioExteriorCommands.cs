using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Pasivos;

// ============================================================================
// I13.3 (86e3ke4cx): autorización en dos niveles de los pagos de comercio
// exterior. La marca de «pasivo de comercio exterior» la pone el listener de CxP
// al proyectar (y el armado de la corrida, por si el gasto se ligó después);
// sin las dos firmas el pasivo no es pagable (PasivosPagables).
// ============================================================================

public static class PagosComercioExterior
{
    public const string EstadoPendienteNivel1 = "PendienteNivel1";
    public const string EstadoPendienteNivel2 = "PendienteNivel2";
    public const string EstadoAutorizado = "Autorizado";
    public const string EstadoRechazado = "Rechazado";

    public static string PermisoNivel1(TipoOperacionComercioExterior tipo) => tipo == TipoOperacionComercioExterior.Importacion
        ? PermisosCanonicos.TesoreriaPagosCeAutorizarImportacion : PermisosCanonicos.TesoreriaPagosCeAutorizarExportacion;

    public static string Estado(AutorizacionPagoComercioExterior a) =>
        a.Rechazada ? EstadoRechazado : a.Autorizada ? EstadoAutorizado : a.NivelPendiente == 1 ? EstadoPendienteNivel1 : EstadoPendienteNivel2;

    /// <summary>Marca como de comercio exterior los pasivos aún sin marca (idempotente; lo persiste el SaveChanges del llamador).</summary>
    public static async Task<bool> MarcarAsync(TesoreriaDbContext db, IPasivoComercioExteriorReadPort? port,
        IEnumerable<(Guid EmpresaId, Guid FacturaProveedorId, Guid? OrdenCompraId)> pasivos, CancellationToken cancellationToken)
    {
        if (port is null) return false;
        var lista = pasivos.ToList();
        var ids = lista.Select(p => p.FacturaProveedorId).ToList();
        var marcados = (await db.AutorizacionesPagoComercioExterior.Where(a => ids.Contains(a.FacturaProveedorId))
            .Select(a => a.FacturaProveedorId).ToListAsync(cancellationToken)).ToHashSet();
        var nuevas = false;
        foreach (var p in lista.Where(p => !marcados.Contains(p.FacturaProveedorId)))
            if (await port.TipoAsync(p.FacturaProveedorId, p.OrdenCompraId, cancellationToken) is { } tipo)
            {
                db.AutorizacionesPagoComercioExterior.Add(new AutorizacionPagoComercioExterior(p.EmpresaId, p.FacturaProveedorId, tipo));
                nuevas = true;
            }
        return nuevas;
    }

    /// <summary>Pago directo y liga tardía: ningún pasivo de comercio exterior sin las dos firmas.</summary>
    public static async Task ExigirAutorizadosAsync(TesoreriaDbContext db, IReadOnlyCollection<Guid> facturaIds, string codigo, CancellationToken cancellationToken)
    {
        var pendiente = await db.AutorizacionesPagoComercioExterior.AsNoTracking()
            .Where(a => facturaIds.Contains(a.FacturaProveedorId) && !(a.Nivel1UsuarioId != null && a.Nivel2UsuarioId != null && a.RechazadoEn == null))
            .Select(a => a.FacturaProveedorId).FirstOrDefaultAsync(cancellationToken);
        if (pendiente != Guid.Empty)
            throw new BusinessRuleException(codigo, $"El pasivo {pendiente} es de comercio exterior y no tiene las dos autorizaciones (nivel 1 y nivel 2).");
    }
}

public sealed record AutorizacionPagoComercioExteriorResponse(
    Guid FacturaProveedorId, string Tipo, string Estado, int? NivelPendiente,
    Guid ProveedorId, string? ProveedorRazonSocial, string? FolioProveedor, decimal SaldoPendiente, string Moneda, DateOnly FechaVencimiento,
    Guid? Nivel1UsuarioId, string? Nivel1Nombre, DateTimeOffset? Nivel1En,
    Guid? Nivel2UsuarioId, string? Nivel2Nombre, DateTimeOffset? Nivel2En,
    string? MotivoRechazo, bool PuedoFirmar, bool PuedoRechazar);

internal static class AutorizacionPagoCeLectura
{
    public static async Task<IReadOnlyList<AutorizacionPagoComercioExteriorResponse>> MapearAsync(
        TesoreriaDbContext db, IProveedorBancoReadPort proveedores, ICorteNombresReadPort? nombres, ICurrentUserPermissions permisos,
        IReadOnlyList<AutorizacionPagoComercioExterior> filas, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var ids = filas.Select(a => a.FacturaProveedorId).ToList();
        var pasivos = await db.PasivosPendientesPago.AsNoTracking().Where(p => ids.Contains(p.FacturaProveedorId)).ToDictionaryAsync(p => p.FacturaProveedorId, ct);
        var info = await proveedores.ObtenerVariosAsync(pasivos.Values.Select(p => p.ProveedorId).Distinct().ToList(), ct);
        var usuarios = filas.SelectMany(a => new[] { a.Nivel1UsuarioId, a.Nivel2UsuarioId }).OfType<Guid>().Distinct().ToList();
        var nombresPorId = nombres is null ? new Dictionary<Guid, string>() : await nombres.NombresCajerosAsync(usuarios, ct);
        var direccion = await permisos.TieneAsync(PermisosCanonicos.TesoreriaPagosCeAutorizarDireccion, ct);
        var importacion = await permisos.TieneAsync(PermisosCanonicos.TesoreriaPagosCeAutorizarImportacion, ct);
        var exportacion = await permisos.TieneAsync(PermisosCanonicos.TesoreriaPagosCeAutorizarExportacion, ct);

        string? Nombre(Guid? id) => id is Guid g && nombresPorId.TryGetValue(g, out var n) ? n : null;
        return filas.Where(a => pasivos.ContainsKey(a.FacturaProveedorId)).Select(a =>
        {
            var p = pasivos[a.FacturaProveedorId];
            info.TryGetValue(p.ProveedorId, out var prov);
            var nivel1 = a.Tipo == TipoOperacionComercioExterior.Importacion ? importacion : exportacion;
            var puedeNivel = a.NivelPendiente == 1 ? nivel1 : a.NivelPendiente == 2 && direccion;
            return new AutorizacionPagoComercioExteriorResponse(
                a.FacturaProveedorId, a.Tipo.ToString(), PagosComercioExterior.Estado(a), a.NivelPendiente,
                p.ProveedorId, prov?.RazonSocial, p.FolioProveedor, p.SaldoPendiente, p.Moneda, p.FechaVencimiento,
                a.Nivel1UsuarioId, Nombre(a.Nivel1UsuarioId), a.Nivel1En, a.Nivel2UsuarioId, Nombre(a.Nivel2UsuarioId), a.Nivel2En,
                a.MotivoRechazo,
                // Quien firmó el nivel 1 ve el nivel 2 deshabilitado en la pantalla; el servidor lo impide (CE_PAGO_MISMA_PERSONA).
                PuedoFirmar: puedeNivel || (a.Rechazada && nivel1),
                PuedoRechazar: a.NivelPendiente is not null && puedeNivel);
        }).ToList();
    }

    public static async Task ExigirFacultadAsync(ICurrentUserPermissions permisos, string permiso, CancellationToken cancellationToken)
    {
        if (!await permisos.TieneAsync(permiso, cancellationToken))
            throw new ForbiddenException("CE_PAGO_SIN_FACULTAD", "No tienes la facultad para esta autorización del pago de comercio exterior.");
    }
}

// --------------------------------------------------- Bandeja por rol

public sealed record ListarAutorizacionesPagoComercioExteriorQuery(string? Estado = null, bool SoloMiNivel = false, int Offset = 0, int Limit = 50)
    : IRequest<PagedResponse<AutorizacionPagoComercioExteriorResponse>>;

public sealed class ListarAutorizacionesPagoComercioExteriorHandler(TesoreriaDbContext db, IProveedorBancoReadPort proveedores,
    ICurrentUserPermissions permisos, ICurrentUserContext user, ICorteNombresReadPort? nombres = null)
    : IRequestHandler<ListarAutorizacionesPagoComercioExteriorQuery, PagedResponse<AutorizacionPagoComercioExteriorResponse>>
{
    public async Task<PagedResponse<AutorizacionPagoComercioExteriorResponse>> Handle(ListarAutorizacionesPagoComercioExteriorQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var limit = Math.Clamp(query.Limit, 1, 200); var offset = Math.Max(0, query.Offset);
        // La bandeja es de quien firma (por rol) y de Tesorería, que la consulta.
        var puede = false;
        foreach (var p in new[] { PermisosCanonicos.TesoreriaPagosCeAutorizarImportacion, PermisosCanonicos.TesoreriaPagosCeAutorizarExportacion,
                     PermisosCanonicos.TesoreriaPagosCeAutorizarDireccion, PermisosCanonicos.TesoreriaPasivosVer })
            puede |= await permisos.TieneAsync(p, ct);
        if (!puede) throw new ForbiddenException("CE_PAGO_SIN_FACULTAD", "No tienes acceso a la bandeja de autorización de pagos de comercio exterior.");

        var q = db.AutorizacionesPagoComercioExterior.AsNoTracking()
            .Where(a => db.PasivosPendientesPago.Any(p => p.FacturaProveedorId == a.FacturaProveedorId && p.SaldoPendiente > 0 && !p.PagoBloqueado));
        q = query.Estado switch
        {
            PagosComercioExterior.EstadoRechazado => q.Where(a => a.RechazadoEn != null),
            PagosComercioExterior.EstadoAutorizado => q.Where(a => a.RechazadoEn == null && a.Nivel1UsuarioId != null && a.Nivel2UsuarioId != null),
            PagosComercioExterior.EstadoPendienteNivel1 => q.Where(a => a.RechazadoEn == null && a.Nivel1UsuarioId == null),
            PagosComercioExterior.EstadoPendienteNivel2 => q.Where(a => a.RechazadoEn == null && a.Nivel1UsuarioId != null && a.Nivel2UsuarioId == null),
            _ => q,
        };
        if (query.SoloMiNivel)
        {
            var imp = await permisos.TieneAsync(PermisosCanonicos.TesoreriaPagosCeAutorizarImportacion, ct);
            var exp = await permisos.TieneAsync(PermisosCanonicos.TesoreriaPagosCeAutorizarExportacion, ct);
            var dir = await permisos.TieneAsync(PermisosCanonicos.TesoreriaPagosCeAutorizarDireccion, ct);
            var yo = user.UserId;
            q = q.Where(a => a.RechazadoEn == null && (
                (a.Nivel1UsuarioId == null && ((imp && a.Tipo == TipoOperacionComercioExterior.Importacion) || (exp && a.Tipo == TipoOperacionComercioExterior.Exportacion)))
                || (dir && a.Nivel1UsuarioId != null && a.Nivel2UsuarioId == null && a.Nivel1UsuarioId != yo)));
        }
        var total = await q.CountAsync(ct);
        var filas = await q.OrderBy(a => a.CreatedAt).Skip(offset).Take(limit).ToListAsync(ct);
        var items = await AutorizacionPagoCeLectura.MapearAsync(db, proveedores, nombres, permisos, filas, ct);
        return new PagedResponse<AutorizacionPagoComercioExteriorResponse>(items, offset, limit, total);
    }
}

// --------------------------------------------------- Autorizar (nivel 1 o 2)

public sealed record AutorizarPagoComercioExteriorCommand(Guid FacturaProveedorId, int Nivel) : IRequest<AutorizacionPagoComercioExteriorResponse>;

public sealed class AutorizarPagoComercioExteriorValidator : AbstractValidator<AutorizarPagoComercioExteriorCommand>
{
    public AutorizarPagoComercioExteriorValidator()
    {
        RuleFor(c => c.FacturaProveedorId).NotEmpty();
        RuleFor(c => c.Nivel).InclusiveBetween(1, 2).WithMessage("El nivel es 1 o 2.");
    }
}

public sealed class AutorizarPagoComercioExteriorHandler(TesoreriaDbContext db, IProveedorBancoReadPort proveedores,
    ICurrentUserPermissions permisos, ICurrentUserContext user, IClock clock, ICorteNombresReadPort? nombres = null)
    : IRequestHandler<AutorizarPagoComercioExteriorCommand, AutorizacionPagoComercioExteriorResponse>
{
    public async Task<AutorizacionPagoComercioExteriorResponse> Handle(AutorizarPagoComercioExteriorCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        if (user.UserId is not Guid usuarioId) throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario que autoriza.");
        var a = await db.AutorizacionesPagoComercioExterior.FirstOrDefaultAsync(x => x.FacturaProveedorId == command.FacturaProveedorId, ct)
            ?? throw new EntityNotFoundException("CE_PAGO_NO_ENCONTRADO", $"El pasivo '{command.FacturaProveedorId}' no es un pago de comercio exterior pendiente de autorización.");
        await AutorizacionPagoCeLectura.ExigirFacultadAsync(permisos,
            command.Nivel == 1 ? PagosComercioExterior.PermisoNivel1(a.Tipo) : PermisosCanonicos.TesoreriaPagosCeAutorizarDireccion, ct);
        if (command.Nivel == 1) a.FirmarNivel1(usuarioId, clock.UtcNow); else a.FirmarNivel2(usuarioId, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return (await AutorizacionPagoCeLectura.MapearAsync(db, proveedores, nombres, permisos, [a], ct)).Single();
    }
}

// --------------------------------------------------- Rechazar

public sealed record RechazarPagoComercioExteriorCommand(Guid FacturaProveedorId, string Motivo) : IRequest<AutorizacionPagoComercioExteriorResponse>;

public sealed class RechazarPagoComercioExteriorValidator : AbstractValidator<RechazarPagoComercioExteriorCommand>
{
    public RechazarPagoComercioExteriorValidator()
    {
        RuleFor(c => c.FacturaProveedorId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class RechazarPagoComercioExteriorHandler(TesoreriaDbContext db, IProveedorBancoReadPort proveedores,
    ICurrentUserPermissions permisos, ICurrentUserContext user, IClock clock, ICorteNombresReadPort? nombres = null)
    : IRequestHandler<RechazarPagoComercioExteriorCommand, AutorizacionPagoComercioExteriorResponse>
{
    public async Task<AutorizacionPagoComercioExteriorResponse> Handle(RechazarPagoComercioExteriorCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        if (user.UserId is not Guid usuarioId) throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario que rechaza.");
        var a = await db.AutorizacionesPagoComercioExterior.FirstOrDefaultAsync(x => x.FacturaProveedorId == command.FacturaProveedorId, ct)
            ?? throw new EntityNotFoundException("CE_PAGO_NO_ENCONTRADO", $"El pasivo '{command.FacturaProveedorId}' no es un pago de comercio exterior pendiente de autorización.");
        // Rechaza quien tiene la facultad del nivel que está pendiente.
        await AutorizacionPagoCeLectura.ExigirFacultadAsync(permisos,
            a.NivelPendiente == 2 ? PermisosCanonicos.TesoreriaPagosCeAutorizarDireccion : PagosComercioExterior.PermisoNivel1(a.Tipo), ct);
        a.Rechazar(usuarioId, command.Motivo, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return (await AutorizacionPagoCeLectura.MapearAsync(db, proveedores, nombres, permisos, [a], ct)).Single();
    }
}
