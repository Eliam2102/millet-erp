using MediatR;

namespace Millet.Compras.Application.Oc.ActualizarReferenciaProveedor;

public sealed record ActualizarReferenciaProveedorCommand(
    Guid OrdenCompraId,
    string? ReferenciaProveedor) : IRequest;
