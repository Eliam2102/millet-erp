using MediatR;
using Millet.Compras.Domain;

namespace Millet.Compras.Application.Oc.Autorizar;

/// <summary>
/// Registra una firma de autorización (N1 o N2). El permiso requerido
/// (<c>compras.ordenes.autorizar-nivel1</c> o
/// <c>compras.ordenes.autorizar-nivel2</c>) se valida en el endpoint Api
/// según <see cref="Nivel"/> — el handler no acopla Compras a Identidad.
/// </summary>
public sealed record AutorizarOrdenCompraCommand(
    Guid OrdenCompraId,
    NivelAutorizacion Nivel,
    string? Notas) : IRequest;
