using MediatR;

namespace Millet.Compras.Application.Oc.ActualizarContactoProveedor;

/// <summary>
/// Actualiza el snapshot del contacto del proveedor. Si los 3 campos
/// son <c>null</c>, se limpia el snapshot completo.
/// </summary>
public sealed record ActualizarContactoProveedorCommand(
    Guid OrdenCompraId,
    string? Nombre,
    string? Email,
    string? Telefono) : IRequest;
