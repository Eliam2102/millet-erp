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

    /// <summary>
    /// C7.3 (ClickUp 86e3jvv6n) — la línea cubre entre el 50 % y el 99.99 %
    /// del pedido: queda en espera de que un gerente de CxC
    /// (<c>cuentas_por_cobrar.liberacion.override</c>) la autorice con
    /// motivo. La pendiente es inmutable; su resolución es una NUEVA
    /// decisión con <c>DecisionOrigenId</c> = la pendiente: al autorizar se
    /// recalcula el crédito (Liberado / Retenido / LiberadoConOverride) y
    /// al rechazar queda <see cref="Retenido"/>.
    /// </summary>
    PendienteAutorizacion = 4,
}

/// <summary>Regla que determinó el resultado (§3.1: crédito / serie / override).</summary>
public enum ReglaAplicadaLiberacion : short
{
    Serie = 1,
    Credito = 2,
    Override = 3,

    /// <summary>
    /// C7.3 — regla de cobertura parcial del crédito (50–99.99 %). Se separa
    /// de <see cref="Credito"/> para que la auditoría distinga «el crédito
    /// no alcanzó y se retuvo» de «alcanzó parcialmente y espera
    /// autorización de CxC».
    /// </summary>
    Cobertura = 4,
}
