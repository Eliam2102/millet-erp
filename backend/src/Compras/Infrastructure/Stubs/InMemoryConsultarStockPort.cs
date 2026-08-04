using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Compras.Domain.Ports.Almacen;

namespace Millet.Compras.Infrastructure.Stubs;

/// <summary>
/// Stub config-driven de <see cref="IConsultarStockPort"/>. Resuelve
/// la disponibilidad como <c>OnHand = ratio</c> con <c>OnHand = 100</c>
/// implícito (escala arbitraria) — no pretende emular un almacén real,
/// solo darle al handler de Autorizar (F4-PR1) un número proporcional
/// para calcular cubrimiento.
///
/// <para>
/// Lookup del ratio: primero <c>Compras:Stubs:Stock:Ratios:{articuloId}</c>;
/// si no existe override, <c>Compras:Stubs:Stock:DefaultRatio</c>.
/// Valores fuera de [0, 1] se truncan a ese rango.
/// </para>
/// <para>
/// <see cref="DisponibilidadStock.Disponible"/> queda igual a
/// <see cref="DisponibilidadStock.OnHand"/> — el stub no modela
/// apartados de stock (las reservas se retiraron en ADR-0047 PR4).
/// </para>
///
/// PLATFORM-TODO(<![CDATA[<StubsTeardown>]]>): borrar cuando Almacén real exista.
/// </summary>
public sealed class InMemoryConsultarStockPort : IConsultarStockPort
{
    public const decimal OnHandEscala = 100m;

    private readonly IOptionsMonitor<ComprasStubsOptions> _options;
    private readonly ILogger<InMemoryConsultarStockPort> _logger;

    public InMemoryConsultarStockPort(
        IOptionsMonitor<ComprasStubsOptions> options,
        ILogger<InMemoryConsultarStockPort> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task<DisponibilidadStock> ConsultarPorSucursalAsync(
        Guid sucursalId,
        Guid articuloId,
        CancellationToken cancellationToken)
    {
        var ratio = ResolverRatio(articuloId);
        var onHand = Math.Round(OnHandEscala * ratio, 4);

        _logger.LogInformation(
            "[InMemoryConsultarStockPort] sucursal={SucursalId} articulo={ArticuloId} ratio={Ratio} onHand={OnHand}",
            sucursalId, articuloId, ratio, onHand);

        return Task.FromResult(new DisponibilidadStock(
            OnHand: onHand,
            Disponible: onHand));
    }

    internal decimal ResolverRatio(Guid articuloId)
    {
        var stock = _options.CurrentValue.Stubs.Stock;
        var key = articuloId.ToString();
        var raw = stock.Ratios.TryGetValue(key, out var override_) ? override_ : stock.DefaultRatio;
        return Math.Clamp(raw, 0m, 1m);
    }
}
