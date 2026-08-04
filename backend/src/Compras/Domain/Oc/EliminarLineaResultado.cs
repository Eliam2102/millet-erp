namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Resultado de <see cref="OrdenCompra.EliminarLinea"/> (F4-PR3). Si la
/// línea eliminada venía de una RQ Y era la última de esa RQ en la OC,
/// <see cref="RequisicionIdALiberar"/> tiene valor — el handler emite
/// <see cref="Events.LineaRqLiberadaEvent"/> y el listener llama
/// <see cref="Domain.Requisicion.LiberarDeOc"/>. Si null, la línea no
/// tenía FK a RQ o aún quedan otras líneas de la misma RQ.
/// </summary>
public sealed record EliminarLineaResultado(Guid? RequisicionIdALiberar);
