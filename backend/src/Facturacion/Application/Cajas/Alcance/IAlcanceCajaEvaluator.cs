namespace Millet.Facturacion.Application.Cajas.Alcance;

/// <summary>
/// Evaluador de la Capa A de Cajas (12-cajas.md §4, §9): resuelve el alcance
/// de datos del usuario actual con <b>resolución dinámica</b> — el mapeo
/// caja↔dimensiones es configuración viva; las dimensiones del documento
/// (sucursal, canal) están congeladas al crearse (`[Decisión 12-2]`).
/// </summary>
public interface IAlcanceCajaEvaluator
{
    /// <summary>
    /// Alcance del usuario actual: <c>Total</c> (permiso
    /// <c>facturacion.caja.leer-todas</c>) | <c>Combinaciones</c> (unión de
    /// sus cajas ACTIVAS — producto sucursales × canales, lado vacío =
    /// comodín — más sus filas de <c>usuario_alcance</c>) | <c>Ninguno</c>.
    /// </summary>
    Task<AlcanceCajas> ResolverAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Bucket "Sin asignar" (`[Decisión 12-B]`): NOT-IN de las combinaciones
    /// de todas las cajas activas de la empresa. Solo tiene sentido para
    /// usuarios con <c>leer-todas</c>; el caller lo garantiza.
    /// </summary>
    Task<AlcanceSinAsignar> ResolverSinAsignarAsync(CancellationToken cancellationToken);
}
