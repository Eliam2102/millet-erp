using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Trazabilidad;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Infrastructure.PublicAdapters;

/// <summary>
/// Facturas de OC y referencias reales de recepción a factura/CFDI.
/// Conserva el filtro de empresa al entrar desde OC o desde factura.
/// </summary>
public sealed class CxpFacturasTrazabilidadProvider : IProveedorNodosTrazabilidad, ICxpFacturasTrazabilidadReadPort
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

        // Mantener el filtro de empresa también al entrar directamente desde factura.

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

    public async Task<NodoArbolDocumento?> ObtenerNodoAsync(TipoDocumentoTrazabilidad tipo, Guid id, CancellationToken ct)
    {
        if (tipo != TipoDocumentoTrazabilidad.FacturaProveedor) return null;
        var filas = await ObtenerPorReferenciaAsync(id, null, ct);
        return filas.Count == 0 ? null : filas[0];
    }

    public async Task<IReadOnlyList<NodoArbolDocumento>> ObtenerAscendentesAsync(TipoDocumentoTrazabilidad tipo, Guid id, CancellationToken ct)
    {
        if (tipo != TipoDocumentoTrazabilidad.FacturaProveedor) return [];
        var ocId = await _db.FacturasProveedor.AsNoTracking().Where(f => f.Id == id)
            .Select(f => f.OrdenCompraId).FirstOrDefaultAsync(ct);
        return ocId is Guid oc ? [new(TipoDocumentoTrazabilidad.OrdenCompra, oc, "", "", default, [], [])] : [];
    }

    public async Task<Guid?> ObtenerCfdiIdAsync(Guid facturaId, CancellationToken ct)
        => await _db.FacturasProveedor.AsNoTracking().Where(f => f.Id == facturaId)
            .Select(f => (Guid?)f.CfdiRecibidoId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<NodoArbolDocumento>> ObtenerPorReferenciaAsync(Guid? facturaId, Guid? cfdiId, CancellationToken ct)
    {
        if (facturaId is null && cfdiId is null) return [];
        var filas = await _db.FacturasProveedor.AsNoTracking()
            .Where(f => (facturaId != null && f.Id == facturaId) || (cfdiId != null && f.CfdiRecibidoId == cfdiId))
            .Select(f => new { f.Id, f.SerieProveedor, f.FolioProveedor, f.Estado, f.FechaDocumento }).ToListAsync(ct);
        return filas.Select(f => new NodoArbolDocumento(TipoDocumentoTrazabilidad.FacturaProveedor,
            f.Id, FormatearFolio(f.SerieProveedor, f.FolioProveedor) ?? "(sin folio)", f.Estado.ToString(), f.FechaDocumento, [], [])).ToList();
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
