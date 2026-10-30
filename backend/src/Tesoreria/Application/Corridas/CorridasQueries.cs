using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Application.Pasivos;
using Millet.Tesoreria.Domain.Corridas;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Corridas;

// ============================================================================
// T9.1-A: lectura de corridas y candidatos. Bruto = subtotal + impuestos
// trasladados; neto = lo que se transfiere (total del CFDI); retenciones del
// CFDI (prorrata si hay pagos parciales). Todo se muestra por proveedor.
// ============================================================================

/// <summary>
/// ÚNICO punto donde se decide si un pasivo es pagable en una corrida. Hoy: autorizado y no retirado por CxP
/// (<c>PasivosPendientesLectura</c>), con saldo y de proveedor (los internos se pagan por su flujo).
/// El filtro de «pago bloqueado» (86e3tactf) y cualquier cambio de elegibilidad de CxP se enchufan aquí.
/// </summary>
public static class PasivosPagables
{
    public static IQueryable<PasivoPendientePago> Elegibles(TesoreriaDbContext db, IReadOnlyList<Guid>? documentosPermitidos) =>
        PasivosPendientesLectura.Consultar(db, documentosPermitidos, soloConSaldo: true)
            .Where(p => p.TipoBeneficiario == PasivoPendientePago.BeneficiarioProveedor);

    /// <summary>Elegibles que no están en ninguna corrida viva.</summary>
    public static IQueryable<PasivoPendientePago> Libres(TesoreriaDbContext db, IReadOnlyList<Guid>? documentosPermitidos) =>
        Elegibles(db, documentosPermitidos)
            .Where(p => !db.Set<CorridaPagoLinea>().Any(l => l.Viva && l.FacturaProveedorId == p.FacturaProveedorId));
}

public sealed record PasivoParaCorridaResponse(
    Guid FacturaProveedorId, Guid ProveedorId, string? ProveedorRazonSocial, string? FolioProveedor,
    DateOnly FechaVencimiento, string Moneda, decimal Bruto, decimal Retenciones, decimal Neto,
    bool CuentaValidada);

public sealed record PasivosParaCorridaQuery(
    string Moneda, Guid? ProveedorId = null, DateOnly? VenceDesde = null, DateOnly? VenceHasta = null,
    int Offset = 0, int Limit = 100)
    : IRequest<PagedResponse<PasivoParaCorridaResponse>>, Millet.SharedKernel.Application.IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "tesoreria.documentos.leer-todas-sucursales";
    public string TipoDocumento => "pasivo";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class PasivosParaCorridaHandler(TesoreriaDbContext db, IProveedorBancoReadPort proveedores)
    : IRequestHandler<PasivosParaCorridaQuery, PagedResponse<PasivoParaCorridaResponse>>
{
    public async Task<PagedResponse<PasivoParaCorridaResponse>> Handle(PasivosParaCorridaQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);
        var q = PasivosPagables.Libres(db, query.DocumentosPermitidos).Where(p => p.Moneda == query.Moneda);
        if (query.ProveedorId is Guid prov) q = q.Where(p => p.ProveedorId == prov);
        if (query.VenceDesde is DateOnly d) q = q.Where(p => p.FechaVencimiento >= d);
        if (query.VenceHasta is DateOnly h) q = q.Where(p => p.FechaVencimiento <= h);

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(p => p.FechaVencimiento).ThenBy(p => p.RecibidoEn).Skip(offset).Take(limit).ToListAsync(ct);
        var info = await proveedores.ObtenerVariosAsync(items.Select(p => p.ProveedorId).Distinct().ToList(), ct);

        var respuesta = items.Select(p =>
        {
            info.TryGetValue(p.ProveedorId, out var pv);
            var (bruto, ret) = p.Desglose(p.SaldoPendiente);
            return new PasivoParaCorridaResponse(p.FacturaProveedorId, p.ProveedorId, pv?.RazonSocial, p.FolioProveedor,
                p.FechaVencimiento, p.Moneda, bruto, ret, p.SaldoPendiente,
                CuentaValidada: pv is { Activo: true, EnRevision: false } && pv.CuentaPara(p.Moneda) is not null);
        }).ToList();
        return new PagedResponse<PasivoParaCorridaResponse>(respuesta, offset, limit, total);
    }
}

public sealed record CorridaProveedorResponse(Guid ProveedorId, string? ProveedorRazonSocial, int Facturas, decimal Bruto, decimal Retenciones, decimal Neto);
public sealed record CorridaLineaResponse(Guid FacturaProveedorId, Guid ProveedorId, string? FolioProveedor, DateOnly? FechaVencimiento,
    decimal Bruto, decimal Retenciones, decimal Neto, bool Ejecutada);
public sealed record CorridaFirmaResponse(Guid UsuarioId, string Roles, DateTimeOffset FirmadaEn);

public sealed record CorridaResponse(
    Guid Id, string Nombre, string Estado, string Moneda, Guid CuentaBancariaId,
    decimal Bruto, decimal Retenciones, decimal Total,
    Guid SolicitadaPor, Guid? AutorizadaPor, DateTimeOffset? AutorizadaEn, DateTimeOffset CreadaEn,
    int FirmasRequeridas, int FirmasRecibidas, string? RolRequerido,
    string? MotivoCierre, string? Avisos, int Version,
    IReadOnlyList<CorridaProveedorResponse> Proveedores,
    IReadOnlyList<CorridaLineaResponse> Lineas,
    IReadOnlyList<CorridaFirmaResponse> Firmas);

internal static class CorridaLectura
{
    public static async Task<CorridaResponse> ToResponseAsync(TesoreriaDbContext db, IProveedorBancoReadPort proveedores, CorridaPago c, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var facturaIds = c.Lineas.Select(l => l.FacturaProveedorId).ToList();
        var pasivos = await db.PasivosPendientesPago.AsNoTracking().Where(p => facturaIds.Contains(p.FacturaProveedorId))
            .Select(p => new { p.FacturaProveedorId, p.FolioProveedor, p.FechaVencimiento }).ToDictionaryAsync(p => p.FacturaProveedorId, ct);
        var info = await proveedores.ObtenerVariosAsync(c.Lineas.Select(l => l.ProveedorId).Distinct().ToList(), ct);

        var lineas = c.Lineas.Select(l =>
        {
            pasivos.TryGetValue(l.FacturaProveedorId, out var p);
            return new CorridaLineaResponse(l.FacturaProveedorId, l.ProveedorId, p?.FolioProveedor, p?.FechaVencimiento,
                l.Bruto, l.Retenciones, l.ImporteProgramado, l.Ejecutada);
        }).ToList();
        var porProveedor = c.Lineas.GroupBy(l => l.ProveedorId).Select(g =>
            new CorridaProveedorResponse(g.Key, info.TryGetValue(g.Key, out var pv) ? pv.RazonSocial : null, g.Count(),
                g.Sum(l => l.Bruto), g.Sum(l => l.Retenciones), g.Sum(l => l.ImporteProgramado))).ToList();
        return new CorridaResponse(c.Id, c.Nombre, c.Estado.ToString(), c.Moneda, c.CuentaBancariaId,
            c.Lineas.Sum(l => l.Bruto), c.Lineas.Sum(l => l.Retenciones), c.Total,
            c.SolicitadaPor, c.AutorizadaPor, c.AutorizadaEn, c.CreadaEn,
            c.FirmasRequeridas, c.FirmasRecibidas, c.RolRequerido, c.MotivoCierre, c.Avisos, c.Version,
            porProveedor, lineas, c.Firmas.Select(f => new CorridaFirmaResponse(f.UsuarioId, f.Roles, f.FirmadaEn)).ToList());
    }
}

public sealed record CorridaResumenResponse(Guid Id, string Nombre, string Estado, string Moneda, decimal Total, int Pasivos,
    int FirmasRequeridas, int FirmasRecibidas, DateTimeOffset CreadaEn, int Version);

public sealed record ListarCorridasQuery(EstadoCorridaPago? Estado = null, int Offset = 0, int Limit = 50)
    : IRequest<PagedResponse<CorridaResumenResponse>>;

public sealed class ListarCorridasHandler(TesoreriaDbContext db) : IRequestHandler<ListarCorridasQuery, PagedResponse<CorridaResumenResponse>>
{
    public async Task<PagedResponse<CorridaResumenResponse>> Handle(ListarCorridasQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var limit = Math.Clamp(query.Limit, 1, 200);
        var offset = Math.Max(0, query.Offset);
        var q = db.CorridasPago.AsNoTracking().AsQueryable();
        if (query.Estado is EstadoCorridaPago e) q = q.Where(c => c.Estado == e);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(c => c.CreadaEn).Skip(offset).Take(limit)
            .Select(c => new CorridaResumenResponse(c.Id, c.Nombre, c.Estado.ToString(), c.Moneda, c.Total, c.Lineas.Count,
                c.FirmasRequeridas, c.FirmasRecibidas, c.CreadaEn, c.Version)).ToListAsync(ct);
        return new PagedResponse<CorridaResumenResponse>(items, offset, limit, total);
    }
}

public sealed record ObtenerCorridaQuery(Guid Id) : IRequest<CorridaResponse>;

public sealed class ObtenerCorridaHandler(TesoreriaDbContext db, IProveedorBancoReadPort proveedores) : IRequestHandler<ObtenerCorridaQuery, CorridaResponse>
{
    public async Task<CorridaResponse> Handle(ObtenerCorridaQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var c = await db.CorridasPago.AsNoTracking().Include(x => x.Lineas).Include(x => x.Firmas).FirstOrDefaultAsync(x => x.Id == query.Id, ct)
            ?? throw new EntityNotFoundException("CORRIDA_NO_ENCONTRADA", $"No se encontró la corrida '{query.Id}'.");
        return await CorridaLectura.ToResponseAsync(db, proveedores, c, ct);
    }
}
