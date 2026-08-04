namespace Millet.CuentasPorCobrar.Domain.Liberacion;

/// <summary>
/// Comportamiento de una serie de folio A+W frente a la liberación
/// (levantamiento §2, catálogo <c>regla_liberacion_serie</c>). Persistido
/// como <c>short</c> con check constraint.
/// </summary>
public enum ComportamientoSerie : short
{
    /// <summary>La serie siempre libera sin evaluar crédito (5000/7000).</summary>
    SiempreLibera = 1,

    /// <summary>La serie nunca libera por crédito — solo con override (3000/4000/8000).</summary>
    NuncaLibera = 2,

    /// <summary>La serie evalúa crédito disponible (default sin regla).</summary>
    EvaluaCredito = 3,
}

/// <summary>Resultado de una <see cref="DecisionLiberacion"/> (§3.1).</summary>
public enum ResultadoLiberacion : short
{
    Liberado = 1,
    Retenido = 2,
    LiberadoConOverride = 3,
}

/// <summary>Regla que determinó el resultado (§3.1: crédito / serie / override).</summary>
public enum ReglaAplicadaLiberacion : short
{
    Serie = 1,
    Credito = 2,
    Override = 3,
}
