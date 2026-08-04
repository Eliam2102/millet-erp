using MediatR;

namespace Millet.Compras.Application.PreviewCubrimiento;

/// <summary>
/// Query read-only del <b>cubrimiento estimado</b> de una RQ (PR-C): para cada
/// línea, cuánto se cubriría de stock de almacén vs. cuánto iría a compra
/// SEGÚN el disponible actual — <b>sin reservar nada ni tocar las columnas
/// persistidas</b>. Es un preview para que el autorizador vea, durante
/// <c>EnAutorizacion</c>, si hay material antes de firmar.
///
/// <para>Solo aplica mientras la RQ está <c>EnAutorizacion</c>; en otros
/// estados el handler devuelve <c>Aplica = false</c> con lista vacía (el
/// cubrimiento real ya está persistido por la bifurcación). Estimación
/// sujeta a disponibilidad hasta autorizar: el stock es móvil.</para>
/// </summary>
public sealed record PreviewCubrimientoQuery(Guid RequisicionId)
    : IRequest<PreviewCubrimientoResponse>;
