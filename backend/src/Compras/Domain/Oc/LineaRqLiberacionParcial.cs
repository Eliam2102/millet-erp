namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Liberación parcial de una línea de RQ tras cancelar una OC con
/// recepciones parciales (F5-PR4). Reporta cuánto se libera de qué RQ
/// para una línea específica de la OC. Permite liberar el compromiso de la
/// RQ sin restaurar las cantidades ya recibidas.
/// </summary>
public sealed record LineaRqLiberacionParcial(
    Guid RequisicionId,
    Guid LineaOrdenCompraId,
    decimal CantidadLiberada);
