using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports;

namespace Millet.Tesoreria.Application.Common;

/// <summary>
/// TES-09 (86e3jvvj4): TC del pago a proveedor, del pago a cuenta y de la confirmación de cobro.
/// <list type="bullet">
///   <item>MXN: no aplica (null), aunque se haya capturado.</item>
///   <item>Otra moneda: el capturado (el que aplicó el banco); si no se capturó, el del catálogo K10.6 a la fecha valor.</item>
///   <item>Sin captura ni catálogo: null; Contabilidad lo busca otra vez en el catálogo y, si sigue sin él, el evento queda en su bandeja.</item>
/// </list>
/// </summary>
public static class TipoCambioDelPago
{
    public const int DecimalesMaximos = 6;

    public static async Task<decimal?> ResolverAsync(ITipoCambioPagoReadPort? catalogo, string moneda, DateOnly fecha,
        decimal? capturado, CancellationToken ct)
    {
        if (string.Equals(moneda, "MXN", StringComparison.OrdinalIgnoreCase)) return null;
        if (capturado is decimal tc) return tc;
        return catalogo is null ? null : await catalogo.ValorEnMxnAsync(moneda, fecha, ct);
    }

    /// <summary>Resuelve y guarda el TC en el movimiento; devuelve el que quedó.</summary>
    public static async Task<decimal?> FijarAsync(MovimientoBancario movimiento, ITipoCambioPagoReadPort? catalogo,
        decimal? capturado, CancellationToken ct, string? motivoCapturado = null)
    {
        movimiento.FijarTipoCambio(await ResolverAsync(catalogo, movimiento.Moneda, movimiento.FechaValor, capturado, ct),
            capturado is null ? null : motivoCapturado);
        return movimiento.TipoCambio;
    }

    /// <summary>Validación del campo capturado: mayor a cero y con 6 decimales como máximo.</summary>
    public static bool EsValido(decimal? tc) => tc is null || (tc > 0 && tc < 1_000_000m && decimal.Round(tc.Value, DecimalesMaximos) == tc);
}
