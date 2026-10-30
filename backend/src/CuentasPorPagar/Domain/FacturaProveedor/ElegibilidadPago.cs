namespace Millet.CuentasPorPagar.Domain.FacturaProveedor;

public sealed record LineaElegibilidad(Guid LineaOcId, decimal Cantidad, decimal PrecioNeto);
public sealed record ResultadoElegibilidad(decimal ElegibleTotal, decimal ElegiblePendiente, decimal Retenido);

public static class ElegibilidadPago
{
    // El neto fiscal sigue la proporción monetaria recibida; al completar recepción libera también los centavos.
    public static ResultadoElegibilidad Calcular(
        IReadOnlyList<LineaElegibilidad> lineas, IReadOnlyDictionary<Guid, decimal> recibidoDisponible,
        decimal totalNeto, decimal anticipos, decimal pagado, decimal descontadoPorDevolucion = 0)
    {
        var baseTotal = lineas.Sum(l => l.Cantidad * l.PrecioNeto);
        var disponible = recibidoDisponible.ToDictionary();
        decimal baseRecibida = 0;
        foreach (var l in lineas)
        {
            var cantidad = Math.Min(l.Cantidad, Math.Max(0, disponible.GetValueOrDefault(l.LineaOcId)));
            baseRecibida += cantidad * l.PrecioNeto;
            disponible[l.LineaOcId] = Math.Max(0, disponible.GetValueOrDefault(l.LineaOcId) - cantidad);
        }
        // G1.4: lo recibido ya viene sin lo devuelto, y la nota de cargo (o la NC que la sustituye) también lo descontó del neto; se
        // suma de vuelta para no descontarlo dos veces. Lo que sobra sobre lo pagable es el IVA esperado, retenido.
        // ponytail: IVA proporcional al neto de la factura; con tasa por línea haría falta una migración (tasa por línea de factura).
        var elegible = baseTotal <= 0 ? 0 : Math.Min(Math.Max(0, totalNeto),
            Math.Round(Math.Max(0, totalNeto + descontadoPorDevolucion) * Math.Min(1, baseRecibida / baseTotal), 2, MidpointRounding.AwayFromZero));
        var pendiente = Math.Max(0, Math.Min(totalNeto - anticipos - pagado, elegible - anticipos - pagado));
        return new(elegible, pendiente, Math.Max(0, totalNeto - anticipos - pagado - pendiente));
    }
}
