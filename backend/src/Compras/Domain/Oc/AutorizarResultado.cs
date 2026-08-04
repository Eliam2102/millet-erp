using Millet.Compras.Domain.Oc.Events;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Resultado de <see cref="OrdenCompra.Autorizar"/>. Devuelve la
/// <see cref="AutorizacionOC"/> recién creada y, si la transición fue
/// a <see cref="EstadoOrdenCompra.Autorizada"/> (N2 exitoso), el
/// evento <see cref="OrdenCompraAutorizadaEvent"/> que el handler
/// publica post-commit via MediatR INotification.
/// </summary>
public sealed record AutorizarResultado(
    AutorizacionOC Autorizacion,
    OrdenCompraAutorizadaEvent? OrdenCompraAutorizada);
