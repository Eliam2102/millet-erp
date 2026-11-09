namespace Millet.Compartido.Domain.Ports;

/// <summary>
/// Elemento del rango de tipos de cambio consultado (I13.2).
/// </summary>
public sealed record TipoCambioRangoItem(string Moneda, DateOnly Fecha, decimal ValorEnMxn, string Origen);

/// <summary>
/// Puerto de lectura de tipo de cambio por rango de fechas y conjunto de monedas (I13.2).
/// Utilizado principalmente para la publicación periódica e idempotente hacia el puente A+W.
/// </summary>
public interface ITipoCambioRangoReadPort
{
    Task<IReadOnlyList<TipoCambioRangoItem>> ObtenerRangoTipoCambioAsync(
        IReadOnlyList<string> monedas, DateOnly desde, DateOnly hasta, CancellationToken ct = default);
}
