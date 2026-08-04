namespace Millet.CentrosCosto.Application.Asignaciones.Alcance;

/// <summary>
/// Evaluador del alcance usuario → máquinas (CECO-PR6, 01-diseno §7).
/// A diferencia de la Capa A de Cajas (reglas con comodines resueltas en
/// vivo), aquí NO hay nada que evaluar: el alcance quedó CONGELADO en
/// filas (usuario_id, dim3_id) al marcar — este evaluador solo resuelve
/// el bypass y lee el set.
/// </summary>
public interface IAlcanceDim3Evaluator
{
    /// <summary>
    /// Alcance del usuario actual: <c>Total</c> (permiso
    /// <c>centros_costo.dim3.leer-todos</c>) | <c>Ids</c> (sus filas de
    /// <c>asignaciones</c>) | <c>Ninguno</c> (sin filas o sin usuario).
    /// </summary>
    Task<AlcanceDim3> ResolverAsync(CancellationToken cancellationToken);
}
