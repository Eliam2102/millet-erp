using MediatR;

namespace Millet.Compras.Application.Oc.EnviarAAutorizacion;

/// <summary>
/// Transmite la OC <c>Borrador</c>/<c>Rechazada</c> a la cadena de
/// autorización. El handler valida las invariantes pre-auth del diseño
/// §7.1 (≥1 línea, proveedor activo C10, cotización adjunta o
/// excepción + correo C11, ficha técnica si importación, motivo +
/// correo si sin-rq) antes de transicionar el estado.
/// </summary>
public sealed record EnviarAAutorizacionOcCommand(Guid OrdenCompraId) : IRequest;
