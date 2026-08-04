using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Infrastructure.Adapters;

/// <summary>
/// Adapter productivo de <see cref="IPuestoReadPort"/> (ADM-PR2,
/// doc 10-catalogo-puestos-empleados). Reemplaza el
/// <c>NoOpPuestoReadPort</c> con seed hardcodeado — cierra
/// PLATFORM-TODO &lt;PuestosEnAdmin&gt;. Los GUIDs del seed viejo se
/// preservaron en <c>compartido.puestos</c> (decisión D6), así que las
/// políticas de viáticos capturadas contra el stub siguen resolviendo.
///
/// <para>Lectura cross-módulo via <see cref="CompartidoDbContext"/> con
/// <c>AsNoTracking</c>. Usa <c>ICurrentEmpresaContext.Bypass()</c> —
/// el catálogo de puestos es cross-empresa.</para>
///
/// <para><b>Mapeo a <see cref="PuestoDto"/></b>: <c>Codigo</c> =
/// <c>Puesto.Clave</c>; <c>Activo</c> = <c>Estatus == Activo</c>.
/// <see cref="ListarAsync"/> devuelve solo puestos activos (es el
/// universo elegible para políticas de viáticos nuevas), ordenados por
/// clave.</para>
/// </summary>
public sealed class PuestoReadPortAdapter : IPuestoReadPort
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public PuestoReadPortAdapter(
        CompartidoDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<PuestoDto?> ObtenerAsync(Guid puestoId, CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();

        return await _db.Puestos
            .AsNoTracking()
            .Where(p => p.Id == puestoId)
            .Select(p => new PuestoDto(
                p.Id,
                p.Clave,
                p.Nombre,
                p.Estatus == EstatusCatalogo.Activo))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PuestoDto>> ListarAsync(CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();

        return await _db.Puestos
            .AsNoTracking()
            .Where(p => p.Estatus == EstatusCatalogo.Activo)
            .OrderBy(p => p.Clave)
            .Select(p => new PuestoDto(p.Id, p.Clave, p.Nombre, true))
            .ToListAsync(cancellationToken);
    }
}
