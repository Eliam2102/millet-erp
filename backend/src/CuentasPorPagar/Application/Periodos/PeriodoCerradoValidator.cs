using Millet.CuentasPorPagar.Domain.Ports.Contabilidad;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Periodos;

public sealed class PeriodoCerradoValidator(IPeriodoContablePort periodos)
{
    public async Task ValidarAsync(IEnumerable<DateOnly> fechas, CancellationToken ct)
    {
        foreach (var fecha in fechas.Distinct().Order())
            if (!await periodos.AdmiteMovimientosAsync(fecha, ct))
                throw new BusinessRuleException("CXP_PERIODO_CERRADO",
                    $"El periodo contable de {fecha:dd/MM/yyyy} está cerrado, no abierto o no existe. Solicita a Contabilidad su apertura o reapertura antes de registrar la operación.");
    }
}
