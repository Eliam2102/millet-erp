namespace Millet.CuentasPorCobrar.Domain.LineaCredito;

/// <summary>
/// Origen del límite de crédito (§3.1 del 00-levantamiento): asegurado
/// por SOLUNION o asignado internamente por Millet.
/// </summary>
public enum OrigenLineaCredito : short
{
    Solunion = 1,
    Interno = 2,
}
