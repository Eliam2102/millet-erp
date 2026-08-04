using Millet.Compras.Domain.Oc.Events;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Resultado de cancelar una OC con recepciones parciales (F5-PR4).
/// Lleva el evento de cancelación principal + las liberaciones parciales
/// por línea para que el handler emita un
/// <see cref="LineaRqLiberadaEvent"/> por cada una con
/// <c>CantidadLiberada</c> populado.
/// </summary>
public sealed record CancelarConRecepcionesResultado(
    OrdenCompraCanceladaEvent EventoCancelada,
    IReadOnlyList<LineaRqLiberacionParcial> LiberacionesParciales);
