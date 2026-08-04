using MediatR;

namespace Millet.Compras.Domain.Events;

/// <summary>
/// Evento informativo (asunción A12 confirmada con cliente) emitido
/// cuando una OC originada en una requisición se cierra entregando
/// menos de lo solicitado. <b>NO</b> abre re-autorización; los saldos
/// no surtidos solo se reportan.
///
/// <para>
/// F5-PR1: lo emite <c>OcCerradaEventHandler</c> al recibir
/// <see cref="Ports.OrdenCompra.OcCerradaEvent"/> con
/// <c>CantidadEntregada &lt; CantidadSolicitada</c>. Compras lo loggea
/// para auditoría operativa; consumers downstream (BI, reportería)
/// pueden suscribirse en fases siguientes.
/// </para>
/// </summary>
public sealed record SaldoNoSurtidoEvent(
    Guid RequisicionId,
    Guid LineaRequisicionId,
    Guid OrdenCompraId,
    Guid EmpresaId,
    decimal CantidadSolicitada,
    decimal CantidadEntregada,
    decimal Saldo,
    DateTimeOffset OcurridoEn) : INotification;
