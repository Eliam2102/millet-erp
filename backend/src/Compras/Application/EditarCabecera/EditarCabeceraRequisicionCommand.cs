using MediatR;
using Millet.Compras.Domain;

namespace Millet.Compras.Application.EditarCabecera;

/// <summary>
/// PATCH parcial sobre la cabecera de una RQ en Borrador (B.4). Los
/// campos nullable que llegan como <c>null</c> NO se modifican. Para
/// limpiar un nullable a <c>null</c>, el FE manda los flags
/// <c>limpiarX = true</c>.
/// </summary>
public sealed record EditarCabeceraRequisicionCommand(
    Guid RequisicionId,
    string? Descripcion,
    DateOnly? FechaEntregaDeseada,
    Prioridad? Prioridad,
    Guid? ProveedorSugeridoId,
    Clasificacion? Clasificacion,
    bool LimpiarDescripcion,
    bool LimpiarFechaEntregaDeseada,
    bool LimpiarProveedorSugeridoId) : IRequest;
