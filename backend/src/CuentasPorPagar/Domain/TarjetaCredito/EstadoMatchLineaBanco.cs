namespace Millet.CuentasPorPagar.Domain.TarjetaCredito;

/// <summary>
/// Estado del match de una línea cruda del banco contra los movimientos
/// capturados (§7 anexo TC, F7-PR5). El algoritmo de match automático
/// asigna <see cref="Pendiente"/> con score 60-89 para sugerencias y
/// <see cref="Matched"/> con score ≥ 90 para match automático.
/// </summary>
public enum EstadoMatchLineaBanco
{
    /// <summary>Score 60-89: sugerencia, requiere confirmación humana.</summary>
    Pendiente               = 1,

    /// <summary>Score ≥ 90: matched automáticamente o confirmado manualmente.</summary>
    Matched                 = 2,

    /// <summary>Score &lt; 60 sin candidatos: requiere captura retroactiva o clasificación.</summary>
    NoConciliado            = 3,

    /// <summary>Línea convertida en movimiento nuevo desde la pantalla (D13).</summary>
    CapturaRetroactiva      = 4,
}
