namespace Millet.Almacen.Domain.Saldos;

/// <summary>Agrupación de la llave del costo medio. Pregunta abierta a Fernando Alejos (M-149): por línea o por sucursal.</summary>
public enum AgrupacionCostoMedio
{
    /// <summary>Por almacén (sucursal + tipo + línea, Lista de almacenes): igual que A+W, que lleva el costo por ubicación. Omisión.</summary>
    PorAlmacen = 0,
    PorSucursal = 1,
}

/// <summary>
/// Costo medio móvil (M-149; Jorge 29-oct, Fernando Alejos 4 y 9-nov): la compra entra a su valor REAL y de ahí sale el promedio
/// del inventario; las entradas manuales toman el promedio del ERP sin moverlo. Unidad: m² en vidrio, cantidad en lo demás; MXN.
/// </summary>
public static class CostoMedio
{
    /// <summary>nuevo = (existencia × promedio + entran × costoCompra) / (existencia + entran). Sin existencia previa el promedio es el costo de la compra.</summary>
    public static decimal Calcular(decimal existencia, decimal promedio, decimal entran, decimal costoCompraMxn)
    {
        if (existencia < 0 || entran < 0 || promedio < 0 || costoCompraMxn < 0)
            throw new ArgumentOutOfRangeException(nameof(existencia), "Existencia, promedio y costo no pueden ser negativos.");
        var total = existencia + entran;
        if (total == 0) return promedio; // nada entra y no hay existencia: se conserva el último
        return decimal.Round((existencia * promedio + entran * costoCompraMxn) / total, 12, MidpointRounding.AwayFromZero);
    }

    /// <summary>Costo de la compra en MXN al tipo de cambio de la propia compra (MXN = 1).</summary>
    public static decimal AMxn(decimal costo, decimal tipoCambio) =>
        tipoCambio <= 0 ? throw new ArgumentOutOfRangeException(nameof(tipoCambio), "El tipo de cambio debe ser mayor a cero.")
                        : decimal.Round(costo * tipoCambio, 12, MidpointRounding.AwayFromZero);

    /// <summary>Promedio vigente de un grupo de saldos: ponderado por existencia; si la existencia es cero conserva el ÚLTIMO promedio (acordado con Fernando, 9-nov). null = nunca hubo saldo.</summary>
    public static (decimal Existencia, decimal? Promedio) Vigente(IReadOnlyCollection<(decimal Cantidad, decimal Costo, DateTimeOffset Fecha)> saldos)
    {
        if (saldos.Count == 0) return (0m, null);
        var existencia = saldos.Sum(s => s.Cantidad);
        return existencia > 0
            ? (existencia, saldos.Sum(s => s.Cantidad * s.Costo) / existencia)
            : (0m, saldos.OrderByDescending(s => s.Fecha).First().Costo);
    }
}
