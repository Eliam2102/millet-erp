using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compras.Domain.Trazabilidad;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Recepciones de OC, sus facturas vinculadas y los ascendentes de recepción/factura.
/// Todas las consultas conservan el filtro de empresa, incluso desde una recepción.
/// </summary>
public sealed class AlmacenRecepcionesTrazabilidadProvider : IProveedorNodosTrazabilidad
{
    private static readonly IReadOnlyList<NodoArbolDocumento> Empty = Array.Empty<NodoArbolDocumento>();

    private readonly AlmacenDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;
    private readonly ICxpFacturasTrazabilidadReadPort _facturas;

    public AlmacenRecepcionesTrazabilidadProvider(
        AlmacenDbContext db,
        ICurrentEmpresaContext empresaContext, ICxpFacturasTrazabilidadReadPort facturas)
    {
        _db = db;
        _empresaContext = empresaContext;
        _facturas = facturas;
    }

    public async Task<IReadOnlyList<NodoArbolDocumento>> ObtenerDescendentesAsync(
        TipoDocumentoTrazabilidad tipoOrigen,
        Guid idOrigen,
        CancellationToken cancellationToken)
    {
        if (tipoOrigen == TipoDocumentoTrazabilidad.Recepcion)
        {
            var m = await _db.Movimientos.AsNoTracking().FirstOrDefaultAsync(m => m.Id == idOrigen && m.Tipo == TipoMovimiento.EntradaCompra, cancellationToken);
            return m is null ? [] : await _facturas.ObtenerPorReferenciaAsync(m.FacturaId, m.CfdiRecibidoId, cancellationToken);
        }
        if (tipoOrigen != TipoDocumentoTrazabilidad.OrdenCompra)
        {
            return Empty;
        }

        // La lectura conserva el filtro de empresa para cualquier entrada.

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
    public async Task<NodoArbolDocumento?> ObtenerNodoAsync(TipoDocumentoTrazabilidad tipo, Guid id, CancellationToken ct)
    {
        if (tipo != TipoDocumentoTrazabilidad.Recepcion) return null;
        var m = await _db.Movimientos.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id && m.Tipo == TipoMovimiento.EntradaCompra, ct);
        return m is null ? null : new(tipo, m.Id, m.Folio ?? "(borrador)", m.Estado.ToString(), m.FechaRegistro, [], []);
    }
    public async Task<IReadOnlyList<NodoArbolDocumento>> ObtenerAscendentesAsync(TipoDocumentoTrazabilidad tipo, Guid id, CancellationToken ct)
    {
        if (tipo == TipoDocumentoTrazabilidad.Recepcion)
        {
            var ocId = await _db.Movimientos.AsNoTracking().Where(m => m.Id == id && m.Tipo == TipoMovimiento.EntradaCompra)
                .Select(m => m.OcId).FirstOrDefaultAsync(ct);
            return ocId is Guid oc ? [new(TipoDocumentoTrazabilidad.OrdenCompra, oc, "", "", default, [], [])] : [];
        }
        if (tipo != TipoDocumentoTrazabilidad.FacturaProveedor) return [];
        var cfdi = await _facturas.ObtenerCfdiIdAsync(id, ct);
        var filas = await _db.Movimientos.AsNoTracking().Where(m => m.Tipo == TipoMovimiento.EntradaCompra
            && (m.FacturaId == id || (cfdi != null && m.CfdiRecibidoId == cfdi)))
            .Select(m => new { m.Id, m.Folio, m.Estado, m.FechaRegistro }).ToListAsync(ct);
        return filas.Select(m => new NodoArbolDocumento(TipoDocumentoTrazabilidad.Recepcion,
            m.Id, m.Folio ?? "(borrador)", m.Estado.ToString(), m.FechaRegistro, [], [])).ToList();
    }

}
