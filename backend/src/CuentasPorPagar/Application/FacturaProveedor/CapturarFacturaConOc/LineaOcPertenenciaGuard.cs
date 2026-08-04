using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc;

/// <summary>
/// Guarda de pertenencia: cada línea de factura que declara una
/// <c>LineaOcId</c> debe referenciar una línea de LA OC que se concilia.
///
/// <para>La atribución por línea alimenta <c>CantidadFacturadaAcumulada</c>
/// en Compras (three-way match por línea de OC); un id ajeno/inexistente
/// corrompería ese acumulado. Es una regla de dominio del handler, no del
/// <c>Validator</c>: el Validator solo ve el comando, mientras que el handler
/// ya tiene las líneas de la OC (<c>oc.Lineas</c> vía
/// <see cref="IComprasOcReadPort"/>). Análoga a <c>OC_PROVEEDOR_MISMATCH</c>.</para>
///
/// <para>Líneas sin <c>LineaOcId</c> (null) son válidas: no atribuyen a una
/// línea específica y no entran en el acumulado por línea.</para>
/// </summary>
internal static class LineaOcPertenenciaGuard
{
    public static void Validar(
        IReadOnlyList<CapturarFacturaConOcLinea> lineasFactura,
        IReadOnlyList<LineaOcDto> lineasOc,
        string folioOc)
    {
        var idsOc = lineasOc.Select(l => l.Id).ToHashSet();

        var ajenas = lineasFactura
            .Where(l => l.LineaOcId is Guid id && !idsOc.Contains(id))
            .Select(l => l.LineaOcId!.Value)
            .Distinct()
            .ToList();

        if (ajenas.Count > 0)
        {
            throw new BusinessRuleException(
                "LINEA_OC_NO_PERTENECE",
                $"La(s) línea(s) de OC [{string.Join(", ", ajenas)}] no pertenece(n) a la OC " +
                $"'{folioOc}'. Cada línea de factura debe vincularse a una línea de la OC conciliada.");
        }
    }
}
