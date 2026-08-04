using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Trazabilidad;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Infrastructure.PublicAdapters;

/// <summary>
/// Provider que aporta nodos de tipo
/// <see cref="TipoDocumentoTrazabilidad.FacturaProveedor"/> al árbol de
/// trazabilidad cuando el origen es una OC. Consulta
/// <c>FacturaProveedor</c> filtrando por <c>OrdenCompraId == idOrigen</c>.
///
/// <para>
/// Bypass de empresa por el mismo motivo que
/// <c>AlmacenRecepcionesTrazabilidadProvider</c>: el servicio de
/// trazabilidad ya validó la empresa al consultar la OC.
/// </para>
/// </summary>
public sealed class CxpFacturasTrazabilidadProvider : IProveedorNodosTrazabilidad
{
    private static readonly IReadOnlyList<NodoArbolDocumento> Empty = Array.Empty<NodoArbolDocumento>();

    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public CxpFacturasTrazabilidadProvider(
        CuentasPorPagarDbContext db,
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

        var facturas = await _db.FacturasProveedor
            .AsNoTracking()
            .Where(f => f.OrdenCompraId == idOrigen)
            .OrderBy(f => f.FechaDocumento)
            .Select(f => new
            {
                f.Id,
                f.FolioProveedor,
                f.SerieProveedor,
                f.Estado,
                f.FechaDocumento,
            })
            .ToListAsync(cancellationToken);

        if (facturas.Count == 0) return Empty;

        return facturas
            .Select(f => new NodoArbolDocumento(
                TipoDocumentoTrazabilidad.FacturaProveedor,
                f.Id,
                FormatearFolio(f.SerieProveedor, f.FolioProveedor) ?? "(sin folio)",
                f.Estado.ToString(),
                f.FechaDocumento,
                Ascendentes: new List<NodoArbolDocumento>(),
                Descendentes: new List<NodoArbolDocumento>()))
            .ToList();
    }

    private static string? FormatearFolio(string? serie, string? folio)
    {
        if (string.IsNullOrWhiteSpace(folio) && string.IsNullOrWhiteSpace(serie))
            return null;
        if (string.IsNullOrWhiteSpace(serie)) return folio;
        if (string.IsNullOrWhiteSpace(folio)) return serie;
        return $"{serie}-{folio}";
    }
}
