using MediatR;

namespace Millet.Compras.Application.Lineas.AgregarLinea;

/// <summary>
/// Comando para agregar una línea a una requisición existente. Solo
/// permitido cuando la requisición está en estado <c>Borrador</c>.
/// La <c>Posicion</c> se asigna automáticamente por el agregado raíz.
/// </summary>
public sealed record AgregarLineaCommand(
    Guid RequisicionId,
    Guid ArticuloId,
    decimal Cantidad,
    string UnidadMedida,
    decimal PrecioEstimadoMonto,
    string PrecioEstimadoMoneda,
    Guid? CuentaContableId = null,
    Guid? CentroCostoId = null,
    string? Proyecto = null,
    DateOnly? FechaRequerida = null,
    string? Notas = null) : IRequest<AgregarLineaResponse>;
