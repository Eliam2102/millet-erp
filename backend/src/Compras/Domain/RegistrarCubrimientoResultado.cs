namespace Millet.Compras.Domain;

/// <summary>
/// Resultado de <see cref="Requisicion.RegistrarCubrimiento"/> (F6-PR3).
/// Empaqueta el evento principal de cubrimiento + el evento opcional
/// de cierre cuando el cubrimiento por almacén alcanza para todas las
/// líneas (transición directa <c>Autorizada → Cerrada</c>).
///
/// <para>
/// <see cref="CerradaEvento"/> es <c>null</c> cuando la RQ queda en
/// <c>EnSurtido</c> (hay saldo a OC). En ese caso el cierre vendrá por
/// la ruta de recepción (F5-PR1).
/// </para>
/// </summary>
public sealed record RegistrarCubrimientoResultado(
    Events.CubrimientoRegistradoEvent CubrimientoEvento,
    Events.RequisicionCerradaEvent? CerradaEvento);
