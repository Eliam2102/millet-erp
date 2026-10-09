using Millet.Almacen.Domain.Ports;
using Millet.Contabilidad.Application.PublicPorts;

namespace Millet.Almacen.Infrastructure.PublicAdapters;

/// <summary>
/// Consulta el calendario de Contabilidad por su puerto público. D9: solo un periodo
/// existente y abierto admite movimientos. El cierre propio de inventario se conserva (D18).
/// </summary>
public sealed class PeriodoContableReadAdapter(IPeriodoContableConsultaPort periodos) : IPeriodoContableReadPort
{
    public async Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken cancellationToken) =>
        (await periodos.ConsultarPorFechaAsync(new DateOnly(año, mes, 1), cancellationToken)).AdmiteMovimientos;
}
