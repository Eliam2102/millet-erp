using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Cancelaciones;

/// <summary>
/// M-188 (86e3zr7kw): efecto de una factura de venta cancelada (o descartada) sobre su pedido y su salida.
/// Una sola pieza para la autorización de la cancelación, el poller del SAT y el descarte. Reglas:
/// <list type="bullet">
/// <item>Pedido vigente: vuelve a Importado y se puede facturar de nuevo (invariante 5); con salida parcial,
/// la salida queda libre porque su factura ya no está vigente. Esto también reabre un pedido que M8 marcó
/// Facturado por parcialidades.</item>
/// <item>Pedido cancelado en origen (A+W): no se toca (antes tronaba con <c>PEDIDO_CANCELADO</c> después de
/// que el SAT ya había cancelado). La salida de la factura se descarta («Pedido cancelado en origen») y no se
/// vuelve a facturar; si la factura era del pedido completo, queda una nota en la bandeja de excepciones.</item>
/// </list>
/// El CFDI, la póliza inversa y la cartera no cambian aquí.
/// </summary>
public static class EfectosCancelacionPedido
{
    public const string MotivoSalidaPedidoCancelado = "Pedido cancelado en origen";

    /// <summary>Aplica el efecto. Devuelve el pedido liberado (vuelto a Importado), o null si no se tocó.</summary>
    public static async Task<PedidoFacturable?> AplicarAsync(
        FacturacionDbContext db, FacturaVenta factura, DateTimeOffset ahora, CancellationToken ct,
        bool soloSiEsLaVigente = false)
    {
        if (factura.PedidoFacturableId is not Guid pedidoId)
            return null;
        var pedido = await db.PedidosFacturables.FirstOrDefaultAsync(p => p.Id == pedidoId, ct);
        if (pedido is null)
            return null;

        if (pedido.Estado == EstadoPedidoFacturable.Cancelado)
        {
            await PedidoCanceladoEnOrigenAsync(db, factura, pedido, ahora, ct);
            return null;
        }

        // El descarte solo libera si ESTA factura tenía tomado el pedido (G5).
        if (soloSiEsLaVigente && pedido.ComprobanteVigenteId != factura.Id)
            return null;

        pedido.RevertirAFacturable();
        // TR-01 / ADR-0048 D3: sin write-back por pedido; la cancelación hacia A+W es otro tipo de fila.
        if (pedido.Origen == OrigenPedido.Aw)
        {
            var control = await db.IngestaControles.FirstOrDefaultAsync(c => c.PedidoFacturableId == pedido.Id, ct);
            control?.CambiarEstado(EstadoIngesta.Importado);
        }
        return pedido;
    }

    private static async Task PedidoCanceladoEnOrigenAsync(
        FacturacionDbContext db, FacturaVenta factura, PedidoFacturable pedido, DateTimeOffset ahora, CancellationToken ct)
    {
        if (factura.SalidaFacturableId is Guid salidaId)
        {
            var salida = await db.SalidasFacturables.FirstOrDefaultAsync(s => s.Id == salidaId, ct);
            salida?.Descartar(MotivoSalidaPedidoCancelado, ahora); // idempotente
            return;
        }

        var pedidoRef = pedido.NumeroPedido ?? pedido.Id.ToString();
        if (pedidoRef.Length > 60) pedidoRef = pedidoRef[..60];
        var detalle = $"Factura cancelada de un pedido cancelado en origen: {factura.Folio}.";
        // El poller puede reaplicar el efecto: no se duplica la nota abierta.
        if (await db.ExcepcionesImportacion.AnyAsync(
                e => e.EmpresaId == pedido.EmpresaId && e.PedidoRef == pedidoRef && !e.Resuelto && e.Detalle == detalle, ct)
            || db.ExcepcionesImportacion.Local.Any(e => e.PedidoRef == pedidoRef && e.Detalle == detalle))
            return;
        db.ExcepcionesImportacion.Add(ExcepcionImportacion.Crear(pedido.EmpresaId, pedido.Origen, pedidoRef, MotivoExcepcion.Otro, detalle));
    }
}
