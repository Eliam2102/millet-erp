using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain.Ports.Administracion;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto
/// <see cref="ISucursalDepartamentoReadPort"/>. Resuelve la asignación
/// N:M leyendo <c>compartido.sucursal_departamentos</c> via
/// <see cref="CompartidoDbContext"/> (tabla introducida por PR-A1 #333).
///
/// <para>
/// Vive en Compras (no en Compartido) porque
/// <c>Millet.Compartido.csproj</c> no referencia Compras y al revés sí —
/// el adapter del consumidor lee del DbContext del data-owner, mismo
/// patrón que <see cref="AlmacenReadAdapter"/> y los otros adapters de
/// esta carpeta hacia Almacén.
/// </para>
///
/// <para>
/// <c>AsNoTracking</c> + <c>ICurrentEmpresaContext.Bypass()</c> — el
/// catálogo es cross-empresa (sin <c>EmpresaId</c>) igual que
/// <c>Sucursal</c> y <c>Departamento</c>.
/// </para>
///
/// <para>
/// <b>Sin cache.</b> La lectura cuesta ~1ms (UNIQUE index sobre
/// <c>(sucursal_id, departamento_id)</c>); el catálogo cambia raramente
/// (sólo via panel admin de PR-A3). Cachear introduciría invalidation
/// cross-handler con los comandos Asignar/Desactivar/Reactivar del PR-A1
/// — overhead innecesario para el throughput esperado del MVP. Si emerge
/// throughput issue post-launch, agregar <c>IMemoryCache</c> con TTL
/// 5min + invalidation desde los handlers admin.
/// </para>
/// </summary>
public sealed class SucursalDepartamentoReadAdapter : ISucursalDepartamentoReadPort
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public SucursalDepartamentoReadAdapter(
        CompartidoDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<bool> OperaAsync(
        Guid sucursalId,
        Guid departamentoId,
        CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();

        return await _db.SucursalDepartamentos
            .AsNoTracking()
            .AnyAsync(
                sd => sd.SucursalId == sucursalId
                   && sd.DepartamentoId == departamentoId
                   && sd.Estatus == EstatusCatalogo.Activo,
                cancellationToken);
    }
}
