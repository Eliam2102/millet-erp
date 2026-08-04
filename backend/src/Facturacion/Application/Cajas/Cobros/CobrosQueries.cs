using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Facturas;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Cajas.Cobros;

// ---- Cobros de una sesión (GET /cobros?sesionId=, permiso caja.operar) ----

public sealed record CobroFormaPagoItem(string FormaPago, decimal Importe, string? Referencia);

public sealed record CobroMostradorItem(
    Guid Id, Guid ComprobanteId, string ComprobanteFolio, string TipoComprobante, string Origen,
    string Estado, decimal Total, string Moneda, DateTimeOffset FechaCobro, Guid UsuarioCobradorId,
    IReadOnlyList<CobroFormaPagoItem> FormasPago);

/// <summary>Cobros registrados en una sesión (panel "Mi caja" / revisión del corte).</summary>
public sealed record ListarCobrosMostradorQuery(Guid SesionId) : IRequest<IReadOnlyList<CobroMostradorItem>>;

public sealed class ListarCobrosMostradorHandler
    : IRequestHandler<ListarCobrosMostradorQuery, IReadOnlyList<CobroMostradorItem>>
{
    private readonly FacturacionDbContext _db;

    public ListarCobrosMostradorHandler(FacturacionDbContext db) => _db = db;

    public async Task<IReadOnlyList<CobroMostradorItem>> Handle(
        ListarCobrosMostradorQuery query, CancellationToken cancellationToken)
    {
        var cobros = await _db.CobrosMostrador.AsNoTracking()
            .Include(c => c.FormasPago)
            .Where(c => c.CajaSesionId == query.SesionId)
            .OrderBy(c => c.FechaCobro)
            .ToListAsync(cancellationToken);

        var comprobanteIds = cobros.Select(c => c.ComprobanteId).Distinct().ToList();
        var comprobantes = await _db.Comprobantes.AsNoTracking()
            .Where(c => comprobanteIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Folio, c.Tipo })
            .ToListAsync(cancellationToken);
        var porId = comprobantes.ToDictionary(c => c.Id);

        return cobros.Select(c =>
        {
            porId.TryGetValue(c.ComprobanteId, out var comprobante);
            return new CobroMostradorItem(
                c.Id, c.ComprobanteId, comprobante?.Folio ?? string.Empty,
                comprobante?.Tipo.ToString() ?? string.Empty, c.Origen.ToString(), c.Estado.ToString(),
                c.Total, c.Moneda, c.FechaCobro, c.UsuarioCobradorId,
                c.FormasPago.Select(f => new CobroFormaPagoItem(f.FormaPago, f.Importe, f.Referencia)).ToList());
        }).ToList();
    }
}

// ---- Comprobantes cobrables (GET /cobros/cobrables, caja.operar, CAJAS-PR7) ----

public sealed record ComprobanteCobrableItem(
    Guid ComprobanteId,
    string Folio,
    string Tipo,
    string ReceptorNombre,
    decimal Total,
    string Moneda,
    DateTimeOffset? FechaTimbrado,
    // [Decisión 13-K]: Total ya viene neto; esto expone cuánto acreditaron
    // las NC timbradas (amortización de anticipos / NC generales) para la UI.
    decimal MontoAcreditado = 0m,
    // RANURA-PR3: porción de MontoAcreditado que corresponde a la NC de la
    // ranura del pedido A+W (motivo Ranura) — el cajero la ve desglosada.
    decimal MontoRanura = 0m);

/// <summary>
/// Comprobantes timbrados sin cobro vigente dentro del alcance del cajero:
/// candidatos del cobro unitario y de la liquidación de ruta (`[12-7]`).
/// Excluye facturas PPD (se cobran vía su REPP), monedas ≠ MXN (P6) y
/// comprobantes sin monto por cobrar (totalmente acreditados por NC). Para
/// el REPP el total mostrado es <c>ImporteTotalPago</c> del complemento;
/// para facturas es el <b>monto por cobrar</b>: Total del CFDI menos las NC
/// timbradas que lo acreditan ([Decisión 13-K]).
/// </summary>
public sealed record ListarComprobantesCobrablesQuery(string? Search = null, int Limite = 50)
    : IRequest<IReadOnlyList<ComprobanteCobrableItem>>;

public sealed class ListarComprobantesCobrablesHandler
    : IRequestHandler<ListarComprobantesCobrablesQuery, IReadOnlyList<ComprobanteCobrableItem>>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;

    public ListarComprobantesCobrablesHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance)
    {
        _db = db;
        _alcance = alcance;
    }

    public async Task<IReadOnlyList<ComprobanteCobrableItem>> Handle(
        ListarComprobantesCobrablesQuery query, CancellationToken cancellationToken)
    {
        var alcance = await _alcance.ResolverAsync(cancellationToken);
        var q = alcance.AplicarA(_db.Comprobantes.AsNoTracking())
            .Where(c => c.Estado == EstadoTimbrado.Timbrado)
            .Where(c => (c.Tipo == TipoComprobante.Ingreso && c.MetodoPago != "PPD" && c.Moneda == "MXN")
                        || c.Tipo == TipoComprobante.Pago)
            .Where(c => !_db.CobrosMostrador.Any(
                x => x.ComprobanteId == c.Id && x.Estado == EstadoCobroMostrador.Registrado));

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            q = q.Where(c => c.Folio.Contains(search) || c.ReceptorNombre.Contains(search));
        }

        var limite = Math.Clamp(query.Limite, 1, 100);
        var items = await q
            .OrderByDescending(c => c.FechaTimbrado)
            .Take(limite)
            .Select(c => new
            {
                c.Id, c.Folio, c.Tipo, c.ReceptorNombre, c.Total, c.Moneda, c.FechaTimbrado,
            })
            .ToListAsync(cancellationToken);

        // El total del REPP (tipo P, Total = 0) vive en el complemento.
        var reppIds = items.Where(i => i.Tipo == TipoComprobante.Pago).Select(i => i.Id).ToList();
        var importesRepp = reppIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _db.RecibosPago.AsNoTracking()
                .Where(r => reppIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.ImporteTotalPago, cancellationToken);

        // NC timbradas que acreditan a las facturas ([Decisión 13-K]).
        var facturaIds = items.Where(i => i.Tipo == TipoComprobante.Ingreso).Select(i => i.Id).ToList();
        var acreditados = await SaldoPorCobrar.AcreditadoPorFacturaAsync(
            _db, facturaIds, cancellationToken);

        // RANURA-PR3: de lo acreditado, cuánto es la NC de la ranura del
        // pedido A+W (motivo Ranura) — el cajero la ve desglosada en la cola.
        var ranuras = facturaIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _db.NotasCredito.AsNoTracking()
                .Where(n => n.FacturaRelacionadaId != null
                            && facturaIds.Contains(n.FacturaRelacionadaId.Value)
                            && n.Estado == EstadoTimbrado.Timbrado
                            && n.Motivo == MotivoNotaCredito.Ranura)
                .GroupBy(n => n.FacturaRelacionadaId!.Value)
                .Select(g => new { FacturaId = g.Key, Total = g.Sum(n => n.Total) })
                .ToDictionaryAsync(x => x.FacturaId, x => x.Total, cancellationToken);

        return items
            .Select(i =>
            {
                var acreditado = i.Tipo == TipoComprobante.Ingreso ? acreditados.GetValueOrDefault(i.Id) : 0m;
                var porCobrar = i.Tipo == TipoComprobante.Pago
                    ? importesRepp.GetValueOrDefault(i.Id)
                    : i.Total - acreditado;
                return new ComprobanteCobrableItem(
                    i.Id, i.Folio, i.Tipo.ToString(), i.ReceptorNombre,
                    porCobrar, i.Moneda, i.FechaTimbrado, acreditado,
                    ranuras.GetValueOrDefault(i.Id));
            })
            .Where(i => i.Total > 0)
            .ToList();
    }
}
