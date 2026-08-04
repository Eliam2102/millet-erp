using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Pasivos;

// ============================================================================
// TES-PR3: bandeja de pasivos autorizados pendientes de pago (§7.2). Fuente:
// proyección `pasivo_pendiente_pago` (solo la escriben el listener de CxP y
// las aplicaciones locales de PR-4). Datos del proveedor (nombre + bancarios)
// vía IProveedorBancoReadPort [T-G1]; la CLABE sale enmascarada salvo
// permiso `tesoreria.movimientos.ver-cuenta-completa`.
// ============================================================================

public sealed record PasivoPendienteResponse(
    Guid FacturaProveedorId,
    Guid ProveedorId,
    string? ProveedorClave,
    string? ProveedorRazonSocial,
    string? Banco,
    string? Clabe,
    string? Beneficiario,
    Guid? OrdenCompraId,
    decimal MontoTotal,
    decimal SaldoPendiente,
    string Moneda,
    decimal? TipoCambio,
    DateOnly FechaVencimiento,
    Guid? UuidCfdi,
    string? FolioProveedor,
    string? MetodoPago,
    DateTimeOffset RecibidoEn,
    Guid? PagoACuentaAbiertoMovimientoId,
    // GI-PR2 (doc 12): pasivos internos — beneficiario empleado / caja de
    // sucursal. "Proveedor" para pasivos de factura.
    string TipoBeneficiario,
    Guid? BeneficiarioId,
    string OrigenTipo,
    Guid OrigenId);

public sealed record BandejaPasivosPendientesQuery(
    Guid? ProveedorId = null,
    string? Moneda = null,
    DateOnly? VenceDesde = null,
    DateOnly? VenceHasta = null,
    decimal? MontoMinimo = null,
    decimal? MontoMaximo = null,
    bool SoloConSaldo = true,
    string? TipoBeneficiario = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<PasivoPendienteResponse>>;

public sealed class BandejaPasivosPendientesHandler
    : IRequestHandler<BandejaPasivosPendientesQuery, PagedResponse<PasivoPendienteResponse>>
{
    private readonly TesoreriaDbContext _db;
    private readonly IProveedorBancoReadPort _proveedores;
    private readonly ICurrentUserPermissions _permissions;

    public BandejaPasivosPendientesHandler(
        TesoreriaDbContext db,
        IProveedorBancoReadPort proveedores,
        ICurrentUserPermissions permissions)
    {
        _db = db; _proveedores = proveedores; _permissions = permissions;
    }

    public async Task<PagedResponse<PasivoPendienteResponse>> Handle(
        BandejaPasivosPendientesQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.PasivosPendientesPago.AsNoTracking();
        if (query.SoloConSaldo) q = q.Where(p => p.SaldoPendiente > 0);
        if (!string.IsNullOrWhiteSpace(query.TipoBeneficiario))
            q = q.Where(p => p.TipoBeneficiario == query.TipoBeneficiario);
        if (query.ProveedorId is Guid proveedor) q = q.Where(p => p.ProveedorId == proveedor);
        if (!string.IsNullOrWhiteSpace(query.Moneda)) q = q.Where(p => p.Moneda == query.Moneda);
        if (query.VenceDesde is DateOnly desde) q = q.Where(p => p.FechaVencimiento >= desde);
        if (query.VenceHasta is DateOnly hasta) q = q.Where(p => p.FechaVencimiento <= hasta);
        if (query.MontoMinimo is decimal min) q = q.Where(p => p.SaldoPendiente >= min);
        if (query.MontoMaximo is decimal max) q = q.Where(p => p.SaldoPendiente <= max);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(p => p.FechaVencimiento).ThenBy(p => p.RecibidoEn)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        // Los internos no tienen proveedor del catálogo — se excluyen del
        // lookup y llevan etiqueta sintética por tipo (GI-PR2).
        var proveedorIds = items
            .Where(p => !p.EsInterno)
            .Select(p => p.ProveedorId)
            .Distinct().ToList();
        var proveedores = await _proveedores.ObtenerVariosAsync(proveedorIds, cancellationToken);

        // TES-PR6 (§3.4 paso 3 / 01-diseño §8.2): sugerencia de liga tardía —
        // si el proveedor tiene un pago a cuenta abierto, la bandeja lo
        // señala para ligar en lugar de re-desembolsar.
        var abiertos = await _db.MovimientosBancarios.AsNoTracking()
                .Where(m => m.Sentido == Domain.Movimientos.SentidoMovimiento.Egreso
                            && m.MotivoNoAplicado != null
                            && m.ContramovimientoDe == null
                            && m.EstadoAplicacion != Domain.Movimientos.EstadoAplicacionMovimiento.Aplicado
                            && m.BeneficiarioRef != null
                            && proveedorIds.Contains(m.BeneficiarioRef.Value))
            .Select(m => new { Proveedor = m.BeneficiarioRef!.Value, m.Id, m.FechaValor })
            .ToListAsync(cancellationToken);
        var pagosACuentaAbiertos = abiertos
            .GroupBy(m => m.Proveedor)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.FechaValor).First().Id);

        var verCompleta = await _permissions.TieneAsync(
            PermisosCanonicos.TesoreriaMovimientosVerCuentaCompleta, cancellationToken);

        var responses = items.Select(p =>
        {
            ProveedorBancoDto? prov = null;
            if (!p.EsInterno) proveedores.TryGetValue(p.ProveedorId, out prov);

            // Etiqueta sintética para internos: el FE actual muestra la
            // razón social — así la bandeja es legible sin cambios de FE
            // (la resolución de nombres reales llega en GI-PR4).
            var razonSocial = p.EsInterno
                ? p.OrigenTipo switch
                {
                    "ReposicionCajaChica" => "Reposición de caja chica",
                    "PrestamoViaticos" => "Préstamo de viáticos",
                    "LiquidacionViaticos" => "Liquidación de viáticos",
                    _ => $"Pasivo interno ({p.OrigenTipo})",
                }
                : prov?.RazonSocial;

            return new PasivoPendienteResponse(
                FacturaProveedorId: p.FacturaProveedorId,
                ProveedorId: p.ProveedorId,
                ProveedorClave: prov?.Clave,
                ProveedorRazonSocial: razonSocial,
                Banco: prov?.Banco,
                Clabe: prov?.Clabe is null ? null : verCompleta ? prov.Clabe : Clabe.Enmascarar(prov.Clabe),
                Beneficiario: prov?.Beneficiario,
                OrdenCompraId: p.OrdenCompraId,
                MontoTotal: p.MontoTotal,
                SaldoPendiente: p.SaldoPendiente,
                Moneda: p.Moneda,
                TipoCambio: p.TipoCambio,
                FechaVencimiento: p.FechaVencimiento,
                UuidCfdi: p.UuidCfdi,
                FolioProveedor: p.FolioProveedor,
                MetodoPago: p.MetodoPago,
                RecibidoEn: p.RecibidoEn,
                PagoACuentaAbiertoMovimientoId: !p.EsInterno
                    && pagosACuentaAbiertos.TryGetValue(p.ProveedorId, out var mov)
                    ? mov
                    : null,
                TipoBeneficiario: p.TipoBeneficiario,
                BeneficiarioId: p.BeneficiarioId,
                OrigenTipo: p.OrigenTipo,
                OrigenId: p.OrigenId);
        }).ToList();

        return new PagedResponse<PasivoPendienteResponse>(responses, offset, limit, total);
    }
}
