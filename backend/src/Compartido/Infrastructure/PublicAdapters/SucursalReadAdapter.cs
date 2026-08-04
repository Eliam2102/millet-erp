using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="ISucursalReadPort"/>
/// declarado en <c>Almacen.Domain.Ports</c>. Reemplaza el
/// <c>NoOpSucursalReadPort</c> de Almacén
/// (PLATFORM-TODO &lt;SucursalReadAdapter&gt;).
///
/// <para>Lectura cross-módulo via <see cref="CompartidoDbContext"/> con
/// <c>AsNoTracking</c>. Usa <c>ICurrentEmpresaContext.Bypass()</c>
/// — sucursales son catálogo cross-empresa.</para>
///
/// <para>Lo invoca el agregado <c>Almacen</c> (F1-PR1) al validar la
/// jerarquía Sucursal → Almacén → Sub-almacén.</para>
///
/// <para><b>Mapeo</b>:</para>
/// <list>
///   <item><c>EsActiva</c> = <see cref="Millet.Administracion.Domain.Sucursal.Estatus"/> == <see cref="EstatusCatalogo.Activo"/></item>
///   <item><c>EmpresaId</c> = <see cref="Guid.Empty"/>: el dominio actual
///     de <c>Sucursal</c> no modela <c>EmpresaId</c> (MVP mono-empresa).
///     Cuando entre la migración multi-empresa, este mapping se
///     actualiza. Los consumidores de Almacén deben tratar
///     <see cref="Guid.Empty"/> como "no aplicable" y NO usarlo para
///     comparar con la empresa actual.</item>
/// </list>
/// </summary>
public sealed class SucursalReadAdapter : ISucursalReadPort
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public SucursalReadAdapter(
        CompartidoDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<SucursalLectura?> ObtenerAsync(
        Guid sucursalId,
        CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();

        var sucursal = await _db.Sucursales
            .AsNoTracking()
            .Where(s => s.Id == sucursalId)
            .Select(s => new SucursalLectura(
                s.Id,
                s.Clave,
                s.Nombre,
                Guid.Empty,
                s.Estatus == EstatusCatalogo.Activo))
            .FirstOrDefaultAsync(cancellationToken);

        return sucursal;
    }
}
