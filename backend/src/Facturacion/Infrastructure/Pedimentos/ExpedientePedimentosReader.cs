using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Application.PublicPorts;
using Millet.Facturacion.Application.Facturas.AplicarPedimento;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Infrastructure.Pedimentos;

/// <summary>
/// M-216: lector real de pedimentos. Para cada factura retenida en <c>PendientePedimento</c> con pedido,
/// toma el pedimento del expediente de Comercio exterior ligado (<see cref="PedimentoDesdeExpediente"/>)
/// y lo entrega al <c>PedimentoSalidasWorker</c> para liberarla. Sin expediente o sin pedimento, la
/// factura sigue retenida y su detalle explica cuál falta.
/// </summary>
public sealed class ExpedientePedimentosReader(FacturacionDbContext db, IEmbarqueExportacionReadPort embarques)
    : ISalidasPedimentosReader
{
    public async Task<IReadOnlyList<PedimentoSalida>> LeerPendientesAsync(int max, CancellationToken cancellationToken)
    {
        var retenidas = await db.FacturasVenta.AsNoTracking()
            .Where(f => f.Estado == EstadoTimbrado.PendientePedimento && f.PedidoFacturableId != null)
            .OrderBy(f => f.CreatedAt)
            .Select(f => new { f.Id, f.PedidoFacturableId, f.SalidaFacturableId })
            .ToListAsync(cancellationToken);

        var resultado = new List<PedimentoSalida>();
        foreach (var f in retenidas)
        {
            if (resultado.Count >= max) break;
            var ofrecido = await PedimentoDesdeExpediente.ConsultarAsync(db, embarques, f.PedidoFacturableId, f.SalidaFacturableId, cancellationToken);
            if (ofrecido.Numero is not null)
                resultado.Add(new PedimentoSalida(f.Id, ofrecido.Numero, ofrecido.Fecha, null));
        }
        return resultado;
    }
}
