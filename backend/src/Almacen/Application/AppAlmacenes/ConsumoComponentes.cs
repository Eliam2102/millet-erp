using Millet.Almacen.Domain.Consumo;

namespace Millet.Almacen.Application.AppAlmacenes;

/// <summary>
/// Un componente de una pieza que A+W marcó terminada (A4.8, D2), leído del origen (solo lectura).
/// <c>ComponenteRef</c> vacío = la pieza no trae componentes legibles. Medidas en mm de la pieza (o de la capa).
/// </summary>
public sealed record PiezaComponenteAw(string Etiqueta, int Operacion, string ComponenteRef, int Capas, string Auftnr, int Pos,
    decimal? AnchoMm, decimal? AltoMm, string? ClaveSucursal, DateTime BearbZeit);

/// <summary>Cálculo del consumo de un componente con su norma (D3, D4). Sin norma o sin dato no inventa: devuelve el motivo.</summary>
public static class ConsumoComponentes
{
    /// <summary>D2: 999970 ensamblado de vidrio cámara (argón y separador); 999980 laminación (PVB).</summary>
    public const int OperacionEnsamblado = 999970;
    public const int OperacionLaminacion = 999980;

    /// <summary>
    /// Cantidad en la unidad del artículo = medida base × consumo por unidad × (1 + merma %). Medida base: m² del área
    /// (M2), m lineales del perímetro (M LIN.), m² × factor de la norma (M³) o 1 por pieza (cualquier otra unidad).
    /// </summary>
    public static (decimal? Cantidad, string? Error) Calcular(string unidadMedida, decimal? anchoMm, decimal? altoMm,
        NormaConsumoComponente? norma, string componenteRef)
    {
        if (norma is null) return (null, $"Sin norma de consumo para el artículo A+W {componenteRef}.");
        var u = new string(unidadMedida.ToUpperInvariant().Replace('²', '2').Replace('³', '3').Where(char.IsLetterOrDigit).ToArray());
        var conMedida = anchoMm is > 0 && altoMm is > 0;
        decimal? Area() => conMedida ? anchoMm!.Value * altoMm!.Value / 1_000_000m : null;
        decimal? Perimetro() => conMedida ? 2m * (anchoMm!.Value + altoMm!.Value) / 1000m : null;

        decimal? baseMedida;
        switch (u)
        {
            case "M2": baseMedida = Area(); break;
            case "MLIN" or "ML" or "M": baseMedida = Perimetro(); break;
            case "M3":
                if (norma.FactorM3PorM2 is not { } factor)
                    return (null, $"La norma del artículo A+W {componenteRef} (M³) no tiene factor m² → m³.");
                baseMedida = Area() * factor;
                break;
            default: baseMedida = 1m; break;
        }
        if (baseMedida is null) return (null, $"La pieza no trae medidas para calcular el consumo del artículo A+W {componenteRef}.");
        // ponytail: redondeo a 4 decimales (precisión de la línea de movimiento); el residuo no se acumula entre piezas.
        var cantidad = Math.Round(baseMedida.Value * norma.ConsumoPorUnidad * (1m + norma.PorcentajeMerma / 100m), 4);
        return cantidad > 0 ? (cantidad, null) : (null, $"El consumo calculado del artículo A+W {componenteRef} es cero.");
    }
}
