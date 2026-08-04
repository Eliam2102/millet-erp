using Millet.SharedKernel.Application.Integration;

namespace Millet.Compras.Application.Integration;

/// <summary>
/// Integration event: la requisición se cerró (todas las líneas con
/// <c>CantidadPendiente == 0</c>). EventType
/// <c>compras.requisicion.cerrada.v1</c>. Se emite en dos paths:
/// <list type="bullet">
///   <item>Stock total cubre todo (F4-PR1, transición directa
///         <c>Autorizada → Cerrada</c>).</item>
///   <item>Última recepción de OC cierra la última línea pendiente
///         (F5-PR1, <c>EnSurtido → Cerrada</c>).</item>
/// </list>
/// </summary>
public sealed record RequisicionCerradaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid RequisicionId)
    : IntegrationEvent("compras.requisicion.cerrada.v1", EmpresaId, OcurridoEn);
