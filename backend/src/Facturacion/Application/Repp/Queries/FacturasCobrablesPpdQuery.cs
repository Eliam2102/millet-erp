using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Facturas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Repp.Queries;

/// <summary>
/// Facturas PPD timbradas con saldo por cobrar &gt; 0: candidatas de un REPP.
/// Alimenta el <c>FacturaPpdPicker</c> del form de emisión (cierra
/// PLATFORM-TODO(&lt;FacturaPicker&gt;)). El saldo es el canónico de
/// <c>[Decisión 13-K]</c>: Total − NC timbradas que acreditan − REPP previos
/// vigentes — exactamente lo que <c>EmitirReppHandler</c> validará después.
/// </summary>
public sealed record FacturasCobrablesPpdQuery(
    string? Search = null, string? ReceptorRfc = null, int Limite = 100)
    : IRequest<IReadOnlyList<FacturaCobrablePpdItem>>;

public sealed record FacturaCobrablePpdItem(
    Guid FacturaVentaId,
    string Folio,
    string ReceptorRfc,
    string ReceptorNombre,
    string Moneda,
    decimal Total,
    decimal AcreditadoNc,
    decimal PagadoRepp,
    decimal Saldo,
    int NumParcialidadSiguiente,
    DateTimeOffset? FechaTimbrado);

public sealed class FacturasCobrablesPpdHandler
    : IRequestHandler<FacturasCobrablesPpdQuery, IReadOnlyList<FacturaCobrablePpdItem>>
{
    // El saldo requiere NC + pagos agregados, así que se filtra en memoria.
    // Las candidatas se recorren en lotes (orden estable) hasta llenar el
    // límite o agotarlas: un corte fijo perdía las PPD vigentes cuando el
    // cliente tenía muchas saldadas más recientes (tarea 86e3tfd8k).
    private const int FactorLote = 3;

    private readonly FacturacionDbContext _db;

    public FacturasCobrablesPpdHandler(FacturacionDbContext db) => _db = db;

    public async Task<IReadOnlyList<FacturaCobrablePpdItem>> Handle(
        FacturasCobrablesPpdQuery query, CancellationToken cancellationToken)
    {
        var limite = Math.Clamp(query.Limite <= 0 ? 100 : query.Limite, 1, 200);

        var q = _db.FacturasVenta.AsNoTracking()
            .Where(f => f.Estado == EstadoTimbrado.Timbrado && f.MetodoPago == "PPD");

        if (!string.IsNullOrWhiteSpace(query.ReceptorRfc))
        {
            var rfc = query.ReceptorRfc.Trim().ToUpperInvariant();
            q = q.Where(f => f.ReceptorRfc == rfc);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            q = q.Where(f => f.Folio.Contains(search)
                             || f.ReceptorNombre.Contains(search)
                             || f.ReceptorRfc.Contains(search));
        }

        var ordenadas = q
            .OrderByDescending(f => f.FechaTimbrado)
            .ThenBy(f => f.Id);
        var lote = limite * FactorLote;
        var resultado = new List<FacturaCobrablePpdItem>(limite);

        for (var saltar = 0; resultado.Count < limite; saltar += lote)
        {
            var candidatas = await ordenadas
                .Skip(saltar)
                .Take(lote)
                .Select(f => new
                {
                    f.Id, f.Folio, f.ReceptorRfc, f.ReceptorNombre, f.Moneda, f.Total, f.FechaTimbrado,
                })
                .ToListAsync(cancellationToken);

            if (candidatas.Count == 0)
                break;

            var ids = candidatas.Select(c => c.Id).ToList();
            var acreditados = await SaldoPorCobrar.AcreditadoPorFacturaAsync(_db, ids, cancellationToken);
            var pagos = await SaldoPorCobrar.PagosVigentes(_db)
                .Where(p => ids.Contains(p.FacturaVentaId))
                .GroupBy(p => p.FacturaVentaId)
                .Select(g => new { FacturaId = g.Key, Pagado = g.Sum(p => p.ImportePagado), Parcialidades = g.Count() })
                .ToDictionaryAsync(x => x.FacturaId, cancellationToken);

            resultado.AddRange(candidatas
                .Select(c =>
                {
                    var acreditado = acreditados.GetValueOrDefault(c.Id);
                    pagos.TryGetValue(c.Id, out var pago);
                    return new FacturaCobrablePpdItem(
                        c.Id, c.Folio, c.ReceptorRfc, c.ReceptorNombre, c.Moneda, c.Total,
                        acreditado, pago?.Pagado ?? 0m,
                        c.Total - acreditado - (pago?.Pagado ?? 0m),
                        (pago?.Parcialidades ?? 0) + 1,
                        c.FechaTimbrado);
                })
                .Where(i => i.Saldo > 0)
                .Take(limite - resultado.Count));

            if (candidatas.Count < lote)
                break;
        }

        return resultado;
    }
}
