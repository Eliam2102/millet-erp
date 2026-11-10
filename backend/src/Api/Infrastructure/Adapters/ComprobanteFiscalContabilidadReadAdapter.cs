using Millet.Contabilidad.Application.ContabilidadElectronica;
using Millet.Contabilidad.Application.Ports;
using Millet.CuentasPorPagar.Application.PublicPorts;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>
/// K10.8: datos del CFDI de cada póliza para la contabilidad electrónica, con los puertos públicos de CxP (recibidos: RFC del emisor)
/// y Facturación (emitidos: RFC del receptor), sin tocar sus tablas.
/// </summary>
public sealed class ComprobanteFiscalContabilidadReadAdapter(ICfdiRecibidoPublicReadPort recibidos, IFacturacionCfdiReadPort emitidos)
    : IComprobanteFiscalContabilidadReadPort
{
    public async Task<IReadOnlyDictionary<string, ComprobanteNacionalContable>> ObtenerAsync(IReadOnlyCollection<string> uuids, CancellationToken ct)
    {
        var res = new Dictionary<string, ComprobanteNacionalContable>(StringComparer.OrdinalIgnoreCase);
        if (uuids.Count == 0) return res;
        foreach (var c in await recibidos.ListarPorUuidsAsync(uuids, ct))
            res[c.Uuid.ToUpperInvariant()] = new(c.Uuid.ToUpperInvariant(), c.RfcEmisor, c.Total, c.Moneda, c.TipoCambio);
        var faltan = uuids.Where(u => !res.ContainsKey(u)).ToList();
        if (faltan.Count > 0)
            foreach (var c in await emitidos.ObtenerPorUuidsAsync(faltan, ct))
                res[c.Uuid] = new(c.Uuid, c.ReceptorRfc, c.Total, c.Moneda, c.TipoCambio);
        return res;
    }
}
