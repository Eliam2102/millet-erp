using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="IAlmacenReadPort"/>. Lee la
/// metadata mínima del almacén físico (Id, Clave, SucursalId, EsActivo)
/// desde <c>almacen.almacenes</c> via <see cref="AlmacenDbContext"/>.
///
/// <para>
/// Vive en Compras (no en Almacén) porque <c>Millet.Almacen.csproj</c>
/// prohíbe referencias a módulos de negocio para evitar ciclos
/// (Compras → Almacén → Compras). Mismo patrón que los otros adapters
/// Compras → Almacén ya residentes en esta carpeta
/// (<see cref="AlmacenEntregasReadAdapter"/>,
/// <see cref="AlmacenStockReadAdapter"/>, etc.).
/// </para>
///
/// <para>
/// Bypass de empresa: <c>almacen.almacenes</c> es catálogo organizacional
/// cross-empresa (no implementa <c>IPerteneceAEmpresa</c>), pero el
/// interceptor requiere bypass explícito cuando no hay <c>HttpContext</c>.
/// Lo invoca <c>CrearRequisicionHandler</c> al validar
/// <c>AlmacenDestinoId</c> de una RQ (PR-A2).
/// </para>
/// </summary>
public sealed class AlmacenReadAdapter : IAlmacenReadPort
{
    private readonly AlmacenDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public AlmacenReadAdapter(
        AlmacenDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<AlmacenLectura?> ObtenerAsync(
        Guid almacenId,
        CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();

        return await _db.Almacenes
            .AsNoTracking()
            .Where(a => a.Id == almacenId)
            .Select(a => new AlmacenLectura(
                a.Id,
                a.Clave,
                a.SucursalId,
                a.Estatus == EstatusCatalogo.Activo))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
