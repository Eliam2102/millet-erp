using Millet.Compras.Domain.Oc.Events;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Resultado de cancelar una OC con recepciones parciales (F5-PR4).
/// Lleva el evento de cancelación principal + las liberaciones parciales
/// por línea. El handler libera el compromiso de las RQ con faltante;
/// el saldo se calcula desde las OC, conservando lo recibido en la cancelada.
/// </summary>
public sealed record CancelarConRecepcionesResultado(
    OrdenCompraCanceladaEvent EventoCancelada,
    IReadOnlyList<LineaRqLiberacionParcial> LiberacionesParciales);
