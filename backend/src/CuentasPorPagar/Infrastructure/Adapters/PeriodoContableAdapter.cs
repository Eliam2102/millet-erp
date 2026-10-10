using Millet.Contabilidad.Application.PublicPorts;
using Millet.CuentasPorPagar.Domain.Ports.Contabilidad;

namespace Millet.CuentasPorPagar.Infrastructure.Adapters;

public sealed class PeriodoContableAdapter(IPeriodoContableConsultaPort periodos) : IPeriodoContablePort
{
    public async Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) =>
        (await periodos.ConsultarPorFechaAsync(fecha, ct)).AdmiteMovimientos;
}
