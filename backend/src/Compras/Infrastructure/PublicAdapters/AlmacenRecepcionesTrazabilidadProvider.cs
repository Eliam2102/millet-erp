using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compras.Domain.Trazabilidad;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Provider que aporta nodos de tipo <see cref="TipoDocumentoTrazabilidad.Recepcion"/>
/// al árbol de trazabilidad cuando el origen es una OC. Consulta
/// <c>almacen.movimientos_inventario</c> filtrando por
/// <c>OcId == idOrigen</c> y tipo <c>EntradaCompra</c>.
///
/// <para>
/// Bypass de empresa: los movimientos no se filtran por la empresa actual
/// (la query corre dentro del servicio de trazabilidad que ya validó la
/// empresa al consultar la OC).
/// </para>
///
/// <para>
/// Solo emite nodos para <c>tipoOrigen == OrdenCompra</c>; otros tipos
/// retornan lista vacía. Cuando llegue trazabilidad desde una recepción
/// (caso opuesto) se agrega aquí.
/// </para>
/// </summary>
public sealed class AlmacenRecepcionesTrazabilidadProvider : IProveedorNodosTrazabilidad
{
    private static readonly IReadOnlyList<NodoArbolDocumento> Empty = Array.Empty<NodoArbolDocumento>();

    private readonly AlmacenDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public AlmacenRecepcionesTrazabilidadProvider(
        AlmacenDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<IReadOnlyList<NodoArbolDocumento>> ObtenerDescendentesAsync(
        TipoDocumentoTrazabilidad tipoOrigen,
        Guid idOrigen,
        CancellationToken cancellationToken)
    {
        if (tipoOrigen != TipoDocumentoTrazabilidad.OrdenCompra)
        {
            return Empty;
        }

        using var bypass = _empresaContext.Bypass();

        var recepciones = await _db.Movimientos
            .AsNoTracking()
            .Where(m => m.OcId == idOrigen && m.Tipo == TipoMovimiento.EntradaCompra)
            .OrderBy(m => m.FechaMovimiento)
            .Select(m => new
            {
                m.Id,
                m.Folio,
                m.Estado,
                m.FechaRegistro,
            })
            .ToListAsync(cancellationToken);

        if (recepciones.Count == 0) return Empty;

        return recepciones
            .Select(r => new NodoArbolDocumento(
                TipoDocumentoTrazabilidad.Recepcion,
                r.Id,
                r.Folio ?? "(borrador)",
                r.Estado.ToString(),
                r.FechaRegistro,
                Ascendentes: new List<NodoArbolDocumento>(),
                Descendentes: new List<NodoArbolDocumento>()))
            .ToList();
    }
}
