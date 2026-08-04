using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="IArticuloReadPort"/>
/// declarado en <c>Almacen.Domain.Ports</c>. Reemplaza el
/// <c>NoOpArticuloReadPort</c> de Almacén
/// (PLATFORM-TODO &lt;ArticuloReadAdapter&gt;).
///
/// <para>Lectura cross-módulo via <see cref="CompartidoDbContext"/> con
/// <c>AsNoTracking</c>. Usa <c>ICurrentEmpresaContext.Bypass()</c> porque
/// el catálogo de artículos es cross-empresa (la entidad no implementa
/// <c>IPerteneceAEmpresa</c>) — defensa explícita siguiendo el patrón
/// de <c>EmpresaResolverAdapter</c>.</para>
///
/// <para><b>Mapeo</b>:</para>
/// <list>
///   <item><c>UnidadMedida</c> ← <see cref="Articulo.UnidadMedidaDefault"/></item>
///   <item><c>EsActivo</c> = <see cref="Articulo.Estatus"/> == <see cref="EstatusCatalogo.Activo"/></item>
///   <item><c>ToleranciaCantidadPorcentaje</c> = <c>null</c>:
///     <see cref="Articulo"/> aún no modela tolerancia por línea. Cuando
///     entre la migración aditiva que la introduce, este mapping se
///     actualiza. Por ahora el handler usa el default global del módulo
///     (A5 del 01-diseño Almacén).</item>
///   <item><c>SubAlmacenDefaultId</c> = <c>null</c>: tampoco modelado en
///     <see cref="Articulo"/> todavía. El handler deja al usuario
///     seleccionar sub-almacén manualmente hasta que se introduzca.</item>
/// </list>
/// </summary>
public sealed class ArticuloReadAdapter : IArticuloReadPort
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public ArticuloReadAdapter(
        CompartidoDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<ArticuloLectura?> ObtenerAsync(
        Guid articuloId,
        CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();

        var articulo = await _db.Articulos
            .AsNoTracking()
            .Where(a => a.Id == articuloId)
            .Select(a => new ArticuloLectura(
                a.Id,
                a.Clave,
                a.Nombre,
                a.UnidadMedidaDefault,
                null,
                null,
                a.Estatus == EstatusCatalogo.Activo,
                a.PrecioReferenciaMonto,
                a.PrecioReferenciaMoneda))
            .FirstOrDefaultAsync(cancellationToken);

        return articulo;
    }

    public async Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
        IReadOnlyCollection<Guid> articuloIds,
        CancellationToken cancellationToken)
    {
        if (articuloIds.Count == 0)
        {
            return new Dictionary<Guid, ArticuloLectura>();
        }

        var distinct = articuloIds.Distinct().ToArray();

        using var bypass = _empresaContext.Bypass();

        return await _db.Articulos
            .AsNoTracking()
            .Where(a => distinct.Contains(a.Id))
            .Select(a => new ArticuloLectura(
                a.Id,
                a.Clave,
                a.Nombre,
                a.UnidadMedidaDefault,
                null,
                null,
                a.Estatus == EstatusCatalogo.Activo,
                a.PrecioReferenciaMonto,
                a.PrecioReferenciaMoneda))
            .ToDictionaryAsync(a => a.Id, cancellationToken);
    }
}
