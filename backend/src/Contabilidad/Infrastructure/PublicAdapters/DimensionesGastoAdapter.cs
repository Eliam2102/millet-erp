using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.SharedKernel.Application;

namespace Millet.Contabilidad.Infrastructure.PublicAdapters;

/// <summary>
/// H1 paso 2 (86e3z5w25) · <see cref="IDimensionesGastoPort"/> para CxP. Quien captura un gasto (empleado, responsable de caja chica)
/// no suele tener permisos de Contabilidad: se abre el bypass del filtro de empresa (ADR-0039) y <see cref="DimensionesGasto"/> acota
/// todo a la empresa recibida.
/// </summary>
public sealed class DimensionesGastoAdapter(DimensionesGasto resolutor, ICurrentEmpresaContext empresa) : IDimensionesGastoPort
{
    public async Task<ExigenciaDimensionesGasto> ExigenciasAsync(Guid empresaId, Guid sucursalId, DateOnly fecha, CancellationToken ct)
    {
        using var bypass = empresa.Bypass();
        return await resolutor.ExigenciasAsync(empresaId, sucursalId, fecha, ct);
    }

    public async Task<IReadOnlyList<CentroGastoOpcion>> CentrosAsync(
        Guid empresaId, Guid sucursalId, int nivel, Guid? dim2Id, string? q, int limite, CancellationToken ct)
    {
        using var bypass = empresa.Bypass();
        return await resolutor.CentrosAsync(empresaId, sucursalId, nivel, dim2Id, q, limite, ct);
    }

    public async Task<string?> ValidarCentroAsync(Guid empresaId, Guid sucursalId, Guid centroCostoId, CancellationToken ct)
    {
        using var bypass = empresa.Bypass();
        return await resolutor.ValidarCentroAsync(empresaId, sucursalId, centroCostoId, ct);
    }
}
