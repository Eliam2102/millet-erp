using Millet.Almacen.Domain.Ports;

namespace Millet.Almacen.Application.Reportes;

/// <summary>F1-MP-01: texto de la medida de hoja para los reportes («2440 × 3660 mm · 20 hojas»); null si la fila no es por medida.</summary>
internal static class MedidaReporte
{
    public static string? Texto(Guid? varianteId, decimal cantidadM2, IReadOnlyDictionary<Guid, VarianteVidrioLectura> medidas)
    {
        if (varianteId is not Guid id) return null;
        if (!medidas.TryGetValue(id, out var m)) return "Medida no disponible";
        var etiqueta = m.AnchoMm is { } a && m.AltoMm is { } h ? $"{a:0.##} × {h:0.##} mm" : m.ClaveVariante;
        return m.AreaHojaM2 is { } area && area > 0
            ? $"{etiqueta} · {Math.Round(cantidadM2 / area, 2):0.##} hojas"
            : etiqueta;
    }
}
