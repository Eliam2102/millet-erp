using Millet.Compras.Domain.Trazabilidad;
using Millet.Tesoreria.Application.PublicPorts;

namespace Millet.Api.Adapters;

public sealed class TesoreriaPagosTrazabilidadProvider(IPagosTrazabilidadReadPort pagos) : IProveedorNodosTrazabilidad
{
    private static NodoArbolDocumento Nodo(PagoTrazabilidad p) => new(TipoDocumentoTrazabilidad.PagoProveedor,
        p.Id, p.Folio, p.Estado, p.Fecha, [], []);
    public async Task<NodoArbolDocumento?> ObtenerNodoAsync(TipoDocumentoTrazabilidad tipo, Guid id, CancellationToken ct)
        => tipo == TipoDocumentoTrazabilidad.PagoProveedor ? (await pagos.ListarAsync(null, id, ct)).Select(Nodo).FirstOrDefault() : null;
    public async Task<IReadOnlyList<NodoArbolDocumento>> ObtenerDescendentesAsync(TipoDocumentoTrazabilidad tipoOrigen, Guid idOrigen, CancellationToken cancellationToken)
        => tipoOrigen == TipoDocumentoTrazabilidad.FacturaProveedor
            ? (await pagos.ListarAsync(idOrigen, null, cancellationToken)).Select(Nodo).ToList() : [];
    public async Task<IReadOnlyList<NodoArbolDocumento>> ObtenerAscendentesAsync(TipoDocumentoTrazabilidad tipo, Guid id, CancellationToken ct)
        => tipo == TipoDocumentoTrazabilidad.PagoProveedor
            ? (await pagos.ListarAsync(null, id, ct)).Select(p => new NodoArbolDocumento(TipoDocumentoTrazabilidad.FacturaProveedor, p.FacturaId, "", "", default, [], [])).ToList() : [];
}
