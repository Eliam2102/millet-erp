using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Millet.Documentos.Componentes;

public enum AlineacionColumna { Izquierda, Centro, Derecha }

public sealed record ColumnaTabla<T>(string EncabezadoEs, Func<T, string?> Valor,
    float AnchoRelativo = 1, AlineacionColumna Alineacion = AlineacionColumna.Izquierda,
    string? EncabezadoEn = null);

public sealed class TablaRenglones<T> : IComponent
{
    private readonly T[] _renglones;
    private readonly ColumnaTabla<T>[] _columnas;
    private readonly Func<T, string?>? _detalle;

    public TablaRenglones(IEnumerable<T> renglones, IEnumerable<ColumnaTabla<T>> columnas,
        Func<T, string?>? detalle = null)
    {
        _renglones = renglones.ToArray();
        _columnas = columnas.ToArray();
        if (_columnas.Length == 0 || _columnas.Any(x => !float.IsFinite(x.AnchoRelativo) || x.AnchoRelativo <= 0))
            throw new ArgumentException("Defina al menos una columna y use anchos relativos mayores que cero.", nameof(columnas));
        _detalle = detalle;
    }

    public void Compose(IContainer container)
    {
        container.Table(tabla =>
        {
            tabla.ColumnsDefinition(definicion =>
            {
                foreach (var columna in _columnas) definicion.RelativeColumn(columna.AnchoRelativo);
            });
            tabla.Header(encabezado =>
            {
                foreach (var columna in _columnas)
                    Alinear(encabezado.Cell().Element(PdfEstilos.Barra), columna.Alineacion)
                        .Text(columna.EncabezadoEs + (string.IsNullOrWhiteSpace(columna.EncabezadoEn) ? "" : "\n" + columna.EncabezadoEn));
            });
            foreach (var renglon in _renglones)
            {
                foreach (var columna in _columnas)
                    Alinear(tabla.Cell().BorderBottom(0.25f).BorderColor(PdfEstilos.GrisBarra).Padding(PdfEstilos.EspaciadoCelda), columna.Alineacion)
                        .Text(columna.Valor(renglon) ?? "");
                var detalle = _detalle?.Invoke(renglon);
                if (!string.IsNullOrWhiteSpace(detalle))
                    tabla.Cell().ColumnSpan((uint)_columnas.Length).PaddingHorizontal(4).PaddingBottom(4)
                        .Text(detalle).FontSize(PdfEstilos.Detalle);
            }
        });
    }

    private static IContainer Alinear(IContainer container, AlineacionColumna alineacion) => alineacion switch
    {
        AlineacionColumna.Centro => container.AlignCenter(),
        AlineacionColumna.Derecha => container.AlignRight(),
        _ => container.AlignLeft()
    };
}
