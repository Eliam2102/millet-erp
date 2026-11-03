namespace Millet.Almacen.Application.Reorden;

/// <summary>Parámetros del cálculo por consumo (sección <c>Reorden</c>). Todo configurable.</summary>
public sealed class ReordenConsumoOptions
{
    public const string SectionName = "Reorden";
    /// <summary>Días de historia de salidas para el consumo promedio.</summary>
    public int DiasHistoria { get; set; } = 90;
    /// <summary>Días de entrega del proveedor (valor único hasta que Millet dé el dato por proveedor/artículo).</summary>
    public int DiasEntrega { get; set; } = 7;
    /// <summary>Días de consumo que el máximo sugerido suma sobre el mínimo.</summary>
    public int DiasColchon { get; set; } = 30;
}

/// <summary>F1-ALM-10: consumo diario, cobertura y sugerencia de mínimo/máximo. Funciones puras.</summary>
public static class ConsumoReorden
{
    public static decimal Diario(decimal salidas, int dias) => dias <= 0 ? 0m : Math.Round(salidas / dias, 4);

    /// <summary>Días que alcanza la existencia al ritmo actual; null si no hay consumo.</summary>
    public static decimal? Cobertura(decimal existencia, decimal consumoDiario)
        => consumoDiario <= 0m ? null : Math.Round(existencia / consumoDiario, 1);

    /// <summary>Mínimo = consumo × días de entrega; máximo = mínimo + consumo × días de colchón. Sin consumo, no sugiere.</summary>
    public static (decimal Minimo, decimal Maximo)? Sugerencia(decimal consumoDiario, int diasEntrega, int diasColchon)
    {
        if (consumoDiario <= 0m) return null;
        var min = Math.Ceiling(consumoDiario * diasEntrega);
        return (min, min + Math.Ceiling(consumoDiario * diasColchon));
    }
}
