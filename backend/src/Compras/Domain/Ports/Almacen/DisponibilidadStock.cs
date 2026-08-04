namespace Millet.Compras.Domain.Ports.Almacen;

/// <summary>
/// DTO de respuesta de <see cref="IConsultarStockPort"/>: snapshot de
/// disponibilidad de un artículo agregada a nivel sucursal (rollup de sus
/// almacenes). Diseño §8.1.
///
/// (PR4 / ADR-0047) Sin reservas: <c>Disponible == OnHand</c> (existencia física).
/// </summary>
public sealed record DisponibilidadStock(
    decimal OnHand,
    decimal Disponible);
