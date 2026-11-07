using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.SharedKernel.Application;

namespace Millet.Contabilidad.Infrastructure.PublicAdapters;

/// <summary>
/// H1 pasos 3 y 4 (86e3z5w25) · <see cref="IDimensionesCapturaPort"/> para Tesorería y Comercio Exterior. Sin empresa en contexto
/// (proceso sin usuario) abre el bypass del filtro global, como el puerto del paso 1 (ADR-0039): si no, las reglas C-03 y los centros
/// no se verían. Con usuario se respeta el filtro normal. Todo lo que lee <see cref="DimensionesCaptura"/> ya se acota a la empresa
/// de la solicitud.
/// </summary>
public sealed class DimensionesCapturaAdapter(DimensionesCaptura resolutor, ICurrentEmpresaContext empresa) : IDimensionesCapturaPort
{
    private IDisposable? SinUsuario() => empresa.Current is null ? empresa.Bypass() : null;

    public async Task<ExigenciaDimensiones> ExigenciasAsync(MovimientoCaptura movimiento, CancellationToken ct)
    {
        using var _ = SinUsuario();
        return await resolutor.ExigenciasAsync(movimiento, ct);
    }

    public async Task<IReadOnlyList<CentroOpcionDto>> CentrosAsync(Guid empresaId, Guid sucursalId, bool equipos, Guid? areaId, string? q,
        int limite, CancellationToken ct)
    {
        using var _ = SinUsuario();
        return await resolutor.CentrosAsync(empresaId, sucursalId, equipos, areaId, q, limite, ct);
    }

    public async Task<DimensionesCapturaResultado> ValidarAsync(MovimientoCaptura movimiento, Guid? areaId, Guid? equipoId, CancellationToken ct)
    {
        using var _ = SinUsuario();
        return await resolutor.ValidarAsync(movimiento, areaId, equipoId, ct);
    }
}
