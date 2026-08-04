using Millet.SharedKernel.Application.Integration;

namespace Millet.Compras.Application.Integration;

/// <summary>
/// Integration event: una OC ligada a la requisición se cerró
/// entregando menos de lo solicitado. <b>Informativo</b> (asunción A12);
/// no abre re-autorización. EventType
/// <c>compras.requisicion.saldoNoSurtido.v1</c>.
/// </summary>
public sealed record RequisicionSaldoNoSurtidoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid RequisicionId,
    Guid LineaRequisicionId,
    Guid OrdenCompraId,
    decimal CantidadSolicitada,
    decimal CantidadEntregada,
    decimal Saldo)
    : IntegrationEvent("compras.requisicion.saldoNoSurtido.v1", EmpresaId, OcurridoEn);
