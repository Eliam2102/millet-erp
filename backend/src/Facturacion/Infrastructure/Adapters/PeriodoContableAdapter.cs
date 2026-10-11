using Millet.Contabilidad.Application.PublicPorts;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.Adapters;

/// <summary>Candado real de Contabilidad, sin consultar tablas de otro módulo.</summary>
public sealed class PeriodoContableAdapter(IPeriodoContableConsultaPort periodos) : IPeriodoContablePort
{
    public async Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken cancellationToken) =>
        (await periodos.ConsultarAsync(año, mes, cancellationToken)).AdmiteMovimientos;
}
