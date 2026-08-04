using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Domain;

/// <summary>
/// Value object inmutable que rastrea cómo se está cubriendo una línea
/// (diseño §4.3): cuánto se consumió de almacén, cuánto se solicitó vía
/// OC, y cuánto ya se recibió. Es la pieza central del modelo
/// stock-aware (Fase 4).
///
/// La <c>CantidadOriginal</c> de la línea NO se persiste duplicada en la
/// BD: vive en <see cref="LineaRequisicion.Cantidad"/> y este VO se
/// construye on-demand vía <see cref="LineaRequisicion.Cubrimiento"/>.
///
/// Reglas (§4.3):
/// <list type="bullet">
///   <item><c>CantidadDeAlmacen + CantidadDeCompra ≤ CantidadOriginal</c></item>
///   <item><c>CantidadRecibida ≤ CantidadDeCompra</c></item>
///   <item>Todas las cantidades ≥ 0.</item>
///   <item><c>CantidadPendiente = CantidadOriginal - CantidadDeAlmacen - CantidadRecibida</c></item>
/// </list>
/// </summary>
public sealed record Cubrimiento
{
    public decimal CantidadOriginal { get; }
    public decimal CantidadDeAlmacen { get; }
    public decimal CantidadDeCompra { get; }
    public decimal CantidadRecibida { get; }

    public decimal CantidadPendiente => CantidadOriginal - CantidadDeAlmacen - CantidadRecibida;

    public Cubrimiento(
        decimal cantidadOriginal,
        decimal cantidadDeAlmacen,
        decimal cantidadDeCompra,
        decimal cantidadRecibida)
    {
        if (cantidadOriginal <= 0)
        {
            throw new BusinessRuleException(
                "CUBRIMIENTO_CANTIDAD_ORIGINAL_INVALIDA",
                "La cantidad original debe ser mayor a cero.");
        }

        if (cantidadDeAlmacen < 0 || cantidadDeCompra < 0 || cantidadRecibida < 0)
        {
            throw new BusinessRuleException(
                "CUBRIMIENTO_CANTIDAD_NEGATIVA",
                "Las cantidades de cubrimiento no pueden ser negativas.");
        }

        if (cantidadDeAlmacen + cantidadDeCompra > cantidadOriginal)
        {
            throw new BusinessRuleException(
                "CUBRIMIENTO_EXCEDE_ORIGINAL",
                $"Cubrimiento ({cantidadDeAlmacen} + {cantidadDeCompra}) excede la cantidad original ({cantidadOriginal}).");
        }

        if (cantidadRecibida > cantidadDeCompra)
        {
            throw new BusinessRuleException(
                "CUBRIMIENTO_RECIBIDA_EXCEDE_COMPRA",
                $"La cantidad recibida ({cantidadRecibida}) excede la cantidad de compra ({cantidadDeCompra}).");
        }

        CantidadOriginal = cantidadOriginal;
        CantidadDeAlmacen = cantidadDeAlmacen;
        CantidadDeCompra = cantidadDeCompra;
        CantidadRecibida = cantidadRecibida;
    }

    /// <summary>
    /// Cubrimiento inicial cuando se crea una línea: 0 en almacén, compra
    /// y recibida. Se actualiza cuando la requisición pasa por el flujo
    /// de bifurcación (Fase 4).
    /// </summary>
    public static Cubrimiento Inicial(decimal cantidadOriginal) =>
        new(cantidadOriginal, 0m, 0m, 0m);

    /// <summary>
    /// Reparte una cantidad solicitada entre stock de almacén y compra, dado
    /// el disponible. Algoritmo conservador: usa todo el disponible hasta el
    /// tope de la cantidad; el resto es saldo para OC. Función <b>pura</b>
    /// (solo aritmética) — <b>fuente única</b> del reparto, compartida por la
    /// bifurcación real al autorizar y por el preview read-only (PR-C). NO
    /// consulta stock ni reserva: el <c>disponible</c> ya viene resuelto por
    /// la capa de Application (vía <c>IConsultarStockPort</c>), de modo que
    /// el dominio no depende de ningún puerto.
    /// </summary>
    /// <param name="disponible">Stock disponible (OnHand − reservado) del artículo.</param>
    /// <param name="cantidad">Cantidad solicitada por la línea.</param>
    /// <returns>Tupla <c>(DeAlmacen, DeCompra)</c> con <c>DeAlmacen + DeCompra = cantidad</c>.</returns>
    public static (decimal DeAlmacen, decimal DeCompra) Repartir(
        decimal disponible,
        decimal cantidad)
    {
        var deAlmacen = Math.Min(disponible, cantidad);
        if (deAlmacen < 0m)
        {
            deAlmacen = 0m;
        }

        return (deAlmacen, cantidad - deAlmacen);
    }

    public bool TieneCubrimiento => CantidadDeAlmacen + CantidadDeCompra > 0;
}
