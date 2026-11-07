using Millet.Contabilidad.Application.Ports;
using Millet.CuentasPorPagar.Application.PublicPorts;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>
/// K10.7: Contabilidad marca el cumplimiento de cada documento de la liquidación de IVA con lo que exponen CxP (método de pago y REP
/// recibido del proveedor) y Facturación (REP timbrado de la factura de venta), sin tocar sus tablas.
/// </summary>
public sealed class CumplimientoIvaReadAdapter(IFacturaProveedorFiscalReadPort cxp, IRepEmitidoReadPort facturacion) : ICumplimientoIvaReadPort
{
    public async Task<IReadOnlyDictionary<Guid, CumplimientoDocumentoIva>> FacturasProveedorAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        (await cxp.ObtenerAsync(ids, ct)).ToDictionary(f => f.Id, f => new CumplimientoDocumentoIva(f.MetodoPago, f.ReppRecibido));

    public async Task<IReadOnlyDictionary<Guid, CumplimientoDocumentoIva>> FacturasVentaAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var conRep = (await facturacion.FacturasConRepAsync(ids, ct)).ToHashSet();
        return ids.Distinct().ToDictionary(id => id, id => new CumplimientoDocumentoIva(null, conRep.Contains(id)));
    }
}
