namespace Millet.Compras.Domain.Matriz;

/// <summary>
/// Resultado de evaluar la matriz de aprobación: cuántos niveles se
/// requieren para autorizar la requisición. Diseño §3.bis.2.
/// </summary>
public enum RequiereNivel
{
    SoloN1 = 1,
    N1YN2 = 2,
}

/// <summary>
/// Evalúa qué nivel(es) de autorización se requieren para una
/// <see cref="Requisicion"/> dada. La implementación v0
/// (<c>EvaluadorMontoVsUmbral</c>) compara el monto total contra el
/// umbral del departamento. F9-PR2 reemplazará por
/// <c>EvaluadorMultidimensional</c> con 3 capas (monto + naturaleza +
/// departamento).
///
/// La interface vive en <see cref="Domain.Matriz"/> aunque la
/// implementación toque la BD: el dominio expone el contrato (puerto).
/// </summary>
public interface IRequiereNivelEvaluator
{
    Task<RequiereNivel> EvaluarAsync(Requisicion requisicion, CancellationToken cancellationToken = default);
}
