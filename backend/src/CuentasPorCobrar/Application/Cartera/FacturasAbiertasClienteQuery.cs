using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Application.Cartera;

// ============================================================================
// CXC-FE-PR6: facturas vivas (Abierta/Parcial) de un cliente para el
// matching depósito ↔ facturas (05-frontend-diseno §4.1). Lee la
// proyección factura_cartera — el saldo es total − pagado − NC, mismo
// criterio que SaldoNetoClienteQuery.
// ============================================================================

public sealed record FacturaAbiertaDto(
    Guid FacturaCarteraId,
    string Uuid,
    string Folio,
    string Moneda,
    string MetodoPago,
    decimal Total,
    decimal MontoPagado,
    decimal MontoNc,
    decimal Saldo,
    DateTimeOffset FechaTimbrado,
    DateTimeOffset FechaVencimiento);

public sealed record FacturasAbiertasClienteQuery(
    Guid ClienteId,
    string? Moneda = null) : IRequest<IReadOnlyList<FacturaAbiertaDto>>;

public sealed class FacturasAbiertasClienteHandler
    : IRequestHandler<FacturasAbiertasClienteQuery, IReadOnlyList<FacturaAbiertaDto>>
{
    private readonly CuentasPorCobrarDbContext _db;
    public FacturasAbiertasClienteHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<IReadOnlyList<FacturaAbiertaDto>> Handle(
        FacturasAbiertasClienteQuery query, CancellationToken cancellationToken)
    {
        var q = _db.FacturasCartera.AsNoTracking()
            .Where(f => f.ClienteId == query.ClienteId
                     && (f.Estado == EstadoFacturaCartera.Abierta
                      || f.Estado == EstadoFacturaCartera.Parcial));
        if (!string.IsNullOrWhiteSpace(query.Moneda))
            q = q.Where(f => f.Moneda == query.Moneda);

        return await q
            .OrderBy(f => f.FechaVencimiento)
            .Select(f => new FacturaAbiertaDto(
                f.Id, f.Uuid, f.Folio, f.Moneda, f.MetodoPago,
                f.Total, f.MontoPagado, f.MontoNc,
                f.Total - f.MontoPagado - f.MontoNc,
                f.FechaTimbrado, f.FechaVencimiento))
            .ToListAsync(cancellationToken);
    }
}
