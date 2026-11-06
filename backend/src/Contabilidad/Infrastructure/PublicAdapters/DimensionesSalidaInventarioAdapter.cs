using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.SharedKernel.Application;

namespace Millet.Contabilidad.Infrastructure.PublicAdapters;

/// <summary>
/// H1 (86e3z5w25) · <see cref="IDimensionesSalidaInventarioPort"/> para el lector de la app de Almacenes, que corre sin usuario:
/// sin contexto de empresa el filtro global ocultaría cuentas, reglas C-03 y centros (ADR-0039, puertos públicos sin contexto HTTP).
/// Todo lo que lee <see cref="DimensionesSalidaInventario"/> ya se acota a la empresa de la solicitud.
/// </summary>
public sealed class DimensionesSalidaInventarioAdapter(DimensionesSalidaInventario resolutor, ICurrentEmpresaContext empresa)
    : IDimensionesSalidaInventarioPort
{
    public async Task<DimensionesSalidaResultado> ResolverAsync(SolicitudDimensionesSalida solicitud, CancellationToken ct)
    {
        using var bypass = empresa.Bypass();
        return await resolutor.ResolverAsync(solicitud, ct);
    }
}
