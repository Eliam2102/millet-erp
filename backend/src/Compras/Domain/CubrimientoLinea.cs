namespace Millet.Compras.Domain;

/// <summary>
/// Input para <see cref="Requisicion.RegistrarCubrimiento"/>: identifica
/// la línea y trae el desglose almacén/compra que el handler de
/// Autorizar calculó tras invocar <c>IConsultarStockPort</c> (F4-PR1).
///
/// <para>
/// El agregado valida que la suma <c>CantidadDeAlmacen + CantidadDeCompra</c>
/// no exceda la cantidad original de la línea (delegado al ctor de
/// <see cref="Cubrimiento"/>).
/// </para>
/// </summary>
public sealed record CubrimientoLinea(
    Guid LineaId,
    decimal CantidadDeAlmacen,
    decimal CantidadDeCompra);
