using Millet.CentrosCosto.Application.PublicPorts;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application;

/// <summary>
/// Guard centralizado para validar elegibilidad de CC-Máquina (Dim3) al guardar líneas
/// en Compras (G1.11 / ADR-0050). Lanza HTTP 422 con código <c>CECO_INVALIDO</c>.
/// </summary>
internal static class CentroCostoLineaGuard
{
    public static async Task ValidarAsync(
        IDim3ElegibilidadPort puerto,
        Guid centroCostoId,
        bool aplicarAlcance,
        CancellationToken cancellationToken)
    {
        var r = await puerto.EvaluarAsync(centroCostoId, aplicarAlcance, cancellationToken);
        if (r == Dim3Elegibilidad.Valida)
        {
            return;
        }

        var motivo = r switch
        {
            Dim3Elegibilidad.NoExiste => "el centro de costo no existe",
            Dim3Elegibilidad.Inactiva => "centro de costo inactivo",
            _ => "centro de costo fuera de su alcance",
        };

        throw new BusinessRuleException("CECO_INVALIDO", $"Centro de costo inválido: {motivo}.");
    }
}
