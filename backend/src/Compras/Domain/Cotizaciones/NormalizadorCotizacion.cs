namespace Millet.Compras.Domain.Cotizaciones;

/// <summary>
/// Normalización de precios de cotización para compararlos: a la unidad de la
/// requisición y a MXN. El factor es cuántas unidades de la RQ equivale una
/// unidad cotizada (CAJA 12 → 12; M2 con hoja de 2.5 m² → 0.4).
/// </summary>
public static class NormalizadorCotizacion
{
    public static decimal PrecioPorUnidadRequisicion(decimal precioUnitario, decimal factorAUnidadRq)
        => Math.Round(precioUnitario / factorAUnidadRq, 4, MidpointRounding.ToEven);

    /// <summary>Precio en MXN; null si la moneda no es MXN y no hay tipo de cambio.</summary>
    public static decimal? PrecioMxn(decimal precioPorUnidadRq, string moneda, decimal? tipoCambioMxn)
    {
        if (moneda == "MXN")
        {
            return Math.Round(precioPorUnidadRq, 2, MidpointRounding.ToEven);
        }

        return tipoCambioMxn is > 0m
            ? Math.Round(precioPorUnidadRq * tipoCambioMxn.Value, 2, MidpointRounding.ToEven)
            : null;
    }
}
