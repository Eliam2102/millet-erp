using MediatR;

namespace Millet.Compras.Domain.Ports.OrdenCompra;

/// <summary>
/// Evento de dominio in-proc emitido por el submódulo OC cuando una
/// orden de compra originada en una requisición se cierra (sin
/// posibilidad de más recepciones). Si la cantidad entregada es menor
/// que la solicitada, hubo saldo no surtido.
///
/// <para>
/// El handler en Compras (<c>OcCerradaEventHandler</c>, F5-PR1)
/// publica <see cref="Domain.Events.SaldoNoSurtidoEvent"/> cuando
/// detecta saldo. Si <c>entregada == solicitada</c>, el evento es
/// no-op (la línea ya se cerró por la recepción correspondiente).
/// </para>
/// <para>
/// F5-PR1 declara el contrato; el emisor real (submódulo OC) y el
/// stub adapter quedan para fases siguientes — por ahora ningún
/// componente lo publica fuera de los tests.
/// </para>
/// </summary>
public sealed record OcCerradaEvent(
    Guid RequisicionId,
    Guid LineaRequisicionId,
    Guid OrdenCompraId,
    Guid EmpresaId,
    decimal CantidadSolicitada,
    decimal CantidadEntregada,
    DateTimeOffset OcurridoEn) : INotification;
