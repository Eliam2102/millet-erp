using MediatR;
using Millet.Facturacion.Domain.Pedidos;

namespace Millet.Facturacion.Application.Pedidos.RegistrarSalidaFacturable;

/// <summary>
/// FAC-08: registra una salida parcial de un pedido para facturarla por separado. El pedido
/// se identifica por id o, si viene de A+W, por su número. Idempotente por origen +
/// referencia: reenviar la misma salida regresa la ya registrada sin cambiar importes. Con
/// <c>OrigenId</c> (id del contenedor de CE) la idempotencia es por origen + documento + pedido.
/// </summary>
public sealed record RegistrarSalidaFacturableCommand(
    Guid? PedidoFacturableId,
    string? NumeroPedidoAw,
    OrigenSalidaFacturable Origen,
    string Referencia,
    IReadOnlyList<SalidaFacturableLineaDto> Lineas,
    Guid? OrigenId = null) : IRequest<SalidaFacturableResponse>;

public sealed record SalidaFacturableLineaDto(int Posicion, decimal Cantidad);

/// <summary><c>YaRegistrada</c>: la salida ya existía con las mismas cantidades (reenvío).</summary>
public sealed record SalidaFacturableResponse(
    Guid Id, Guid PedidoFacturableId, int NumeroParcialidad, OrigenSalidaFacturable Origen, string Referencia,
    decimal Total, bool YaRegistrada, IReadOnlyList<SalidaFacturableLineaResponse> Lineas)
{
    public static SalidaFacturableResponse De(SalidaFacturable s, bool yaRegistrada) =>
        new(s.Id, s.PedidoFacturableId, s.NumeroParcialidad, s.Origen, s.Referencia, s.Total, yaRegistrada,
            s.Lineas.OrderBy(l => l.Posicion).Select(l => new SalidaFacturableLineaResponse(l.Posicion, l.Cantidad, l.Importe)).ToList());
}

public sealed record SalidaFacturableLineaResponse(int Posicion, decimal Cantidad, decimal Importe);
