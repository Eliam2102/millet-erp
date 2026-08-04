namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Liberación parcial de una línea de RQ tras cancelar una OC con
/// recepciones parciales (F5-PR4). Reporta cuánto se libera de qué RQ
/// para una línea específica de la OC; el handler convierte cada entrada
/// en un <see cref="Events.LineaRqLiberadaEvent"/> con
/// <c>CantidadLiberada</c> populado.
/// </summary>
public sealed record LineaRqLiberacionParcial(
    Guid RequisicionId,
    Guid LineaOrdenCompraId,
    decimal CantidadLiberada);
