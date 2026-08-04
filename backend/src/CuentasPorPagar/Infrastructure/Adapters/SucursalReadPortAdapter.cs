using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Infrastructure.Adapters;

/// <summary>
/// Adapter productivo de <see cref="ISucursalReadPort"/>. Reemplaza
/// <see cref="Stubs.NoOpSucursalReadPort"/> (PLATFORM-TODO &lt;SucursalReadPort&gt;).
///
/// <para>Lectura cross-módulo via <see cref="CompartidoDbContext"/> con
/// <c>AsNoTracking</c>. Usa <c>ICurrentEmpresaContext.Bypass()</c> porque
/// el catálogo de sucursales es cross-empresa.</para>
///
/// <para>
/// Lo invoca CxP al capturar facturas (sucursal del gasto), comprobaciones
/// y notas de cargo (§6.1 del 01-diseno).
/// </para>
///
/// <para><b>Mapeo a <see cref="SucursalDto"/></b>:</para>
/// <list>
///   <item><c>Codigo</c> = <see cref="Millet.Administracion.Domain.Sucursal.Clave"/>.</item>
///   <item><c>Activa</c> = <see cref="Millet.Administracion.Domain.Sucursal.Estatus"/> == <see cref="EstatusCatalogo.Activo"/>.</item>
///   <item><c>EmpresaId</c> = <see cref="Guid.Empty"/>: el dominio actual
///   de <c>Sucursal</c> no modela <c>EmpresaId</c> (MVP mono-empresa).
///   Consumidores deben tratar <see cref="Guid.Empty"/> como "no aplicable".</item>
/// </list>
///
/// <para><b><see cref="ListarPorEmpresaAsync"/></b>: hasta que
/// <c>Sucursal</c> modele <c>EmpresaId</c>, retorna TODAS las sucursales
/// activas del catálogo (consistente con el comportamiento mono-empresa).
/// Cuando la migración multi-empresa entre, este método filtrará por la
/// columna real.</para>
/// </summary>
public sealed class SucursalReadPortAdapter : ISucursalReadPort
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public SucursalReadPortAdapter(
        CompartidoDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<SucursalDto?> ObtenerAsync(Guid sucursalId, CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();

        return await _db.Sucursales
            .AsNoTracking()
            .Where(s => s.Id == sucursalId)
            .Select(s => new SucursalDto(
                s.Id,
                Guid.Empty,
                s.Clave,
                s.Nombre,
                s.Estatus == EstatusCatalogo.Activo))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SucursalDto>> ListarPorEmpresaAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();

        return await _db.Sucursales
            .AsNoTracking()
            .Where(s => s.Estatus == EstatusCatalogo.Activo)
            .OrderBy(s => s.Clave)
            .Select(s => new SucursalDto(
                s.Id,
                Guid.Empty,
                s.Clave,
                s.Nombre,
                true))
            .ToListAsync(cancellationToken);
    }
}
