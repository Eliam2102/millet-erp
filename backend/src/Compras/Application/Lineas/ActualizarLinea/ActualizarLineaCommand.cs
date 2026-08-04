using MediatR;

namespace Millet.Compras.Application.Lineas.ActualizarLinea;

/// <summary>
/// Comando para reemplazar los campos estructurales de una línea
/// existente. Replace completo (no PATCH parcial); el cliente envía
/// todos los campos. Solo permitido en estado <c>Borrador</c> y si la
/// línea NO tiene cubrimiento (§4.2 invariante).
/// </summary>
public sealed record ActualizarLineaCommand(
    Guid RequisicionId,
    Guid LineaId,
    Guid ArticuloId,
    decimal Cantidad,
    string UnidadMedida,
    decimal PrecioEstimadoMonto,
    string PrecioEstimadoMoneda,
    Guid? CuentaContableId = null,
    Guid? CentroCostoId = null,
    string? Proyecto = null,
    DateOnly? FechaRequerida = null) : IRequest<Unit>;
