namespace Millet.Integraciones.Aw.Application.Ports;

/// <summary>
/// Falla leyendo el endpoint <c>GET /completions</c> del drop service
/// on-prem (vía Hybrid Connection). <see cref="IsTransient"/> distingue
/// entre fallas reintentables (red, 5xx, timeout) y permanentes (auth
/// rota, configuración inválida).
///
/// <para>
/// Análoga a <see cref="AwDropException"/> (mismo módulo, mismo
/// servicio on-prem), pero específica para el endpoint read-only de
/// completions — el caller la captura de forma separada para no mezclar
/// errores de drop con errores de reconciliación tardía.
/// </para>
/// </summary>
public sealed class AwCompletionsException : Exception
{
    /// <summary>
    /// True si el caller puede reintentar (red transitoria, 5xx, timeout).
    /// False para errores permanentes (auth rota, configuración inválida)
    /// — el worker debe loggear y dejar que el operador resuelva.
    /// </summary>
    public bool IsTransient { get; }

    /// <summary>
    /// Categoría corta del error para tagging en métricas/logs.
    /// Valores típicos: <c>"http_5xx"</c>, <c>"http_401"</c>, <c>"network"</c>,
    /// <c>"timeout"</c>, <c>"invalid_response"</c>, <c>"invalid_api_key"</c>.
    /// </summary>
    public string Kind { get; }

    public AwCompletionsException(string message, string kind, bool isTransient)
        : base(message)
    {
        Kind = kind;
        IsTransient = isTransient;
    }

    public AwCompletionsException(string message, string kind, bool isTransient, Exception inner)
        : base(message, inner)
    {
        Kind = kind;
        IsTransient = isTransient;
    }
}
