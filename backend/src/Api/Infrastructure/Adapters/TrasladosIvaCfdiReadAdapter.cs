using Millet.Contabilidad.Application.Ports;
using Millet.CuentasPorPagar.Application.PublicPorts;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>M-209 (CA4): traslados de IVA del XML de un CFDI recibido, con el puerto público de CxP (sin tocar sus tablas).</summary>
public sealed class TrasladosIvaCfdiReadAdapter(ICfdiRecibidoPublicReadPort recibidos) : ITrasladosIvaCfdiReadPort
{
    public async Task<IReadOnlyList<TrasladoIvaDocumento>?> LeerAsync(string uuid, CancellationToken ct) =>
        await recibidos.LeerTrasladosIvaAsync(uuid, ct) is { } traslados
            ? [.. traslados.Select(t => new TrasladoIvaDocumento(t.Tratamiento, t.Base, t.Iva))]
            : null;
}
