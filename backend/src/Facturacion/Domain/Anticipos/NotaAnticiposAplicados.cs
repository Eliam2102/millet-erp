using System.Globalization;

namespace Millet.Facturacion.Domain.Anticipos;

/// <summary>
/// Texto prellenado de las observaciones de una factura con los anticipos
/// aplicados (86e3tfdw2). El cajero lo puede editar antes de emitir; se
/// imprime en el PDF y nunca va al XML del CFDI. Formato es-MX explícito
/// (coma de miles, punto decimal) para no depender de la cultura del servidor.
/// </summary>
public static class NotaAnticiposAplicados
{
    private const string Encabezado = "Anticipos aplicados: ";

    private static readonly NumberFormatInfo FormatoMx = new()
    {
        NumberDecimalSeparator = ".",
        NumberGroupSeparator = ",",
        NumberGroupSizes = [3],
    };

    /// <summary>
    /// «Anticipos aplicados: FANT-12 $5,000.00 MXN; FANT-15 $1,200.00 MXN».
    /// Cadena vacía si no hay anticipos.
    /// </summary>
    public static string Componer(IEnumerable<(string Folio, decimal Importe, string Moneda)> anticipos)
    {
        ArgumentNullException.ThrowIfNull(anticipos);

        var partes = anticipos
            .Select(a =>
            {
                var importe = Math.Round(a.Importe, 2, MidpointRounding.AwayFromZero).ToString("N2", FormatoMx);
                return $"{a.Folio.Trim()} ${importe} {a.Moneda.Trim().ToUpperInvariant()}";
            })
            .ToList();

        return partes.Count == 0 ? string.Empty : Encabezado + string.Join("; ", partes);
    }
}
