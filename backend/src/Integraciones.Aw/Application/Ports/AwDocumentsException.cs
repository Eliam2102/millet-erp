namespace Millet.Integraciones.Aw.Application.Ports;

/// <summary>
/// Falla leyendo los endpoints <c>GET /documents</c> /
/// <c>GET /documents/{filename}</c> del drop service on-prem (vía Hybrid
/// Connection). <see cref="IsTransient"/> distingue entre fallas
/// reintentables (red, 5xx, timeout) y permanentes (auth rota,
/// configuración inválida).
///
/// <para>
/// Análoga a <see cref="AwCompletionsException"/> (mismo módulo, mismo
/// servicio on-prem), pero específica para los endpoints de documentos
/// PDF — el caller la captura por separado para no mezclar errores de
/// sincronización de PDF con los de reconciliación tardía.
/// </para>
/// </summary>
public sealed class AwDocumentsException : Exception
{
    /// <summary>
    /// True si el caller puede reintentar (red transitoria, 5xx, timeout).
    /// False para errores permanentes (auth rota, configuración inválida).
    /// </summary>
    public bool IsTransient { get; }

    /// <summary>
    /// Categoría corta del error para tagging en métricas/logs.
    /// Valores típicos: <c>"http_5xx"</c>, <c>"http_401"</c>, <c>"network"</c>,
    /// <c>"timeout"</c>, <c>"invalid_response"</c>, <c>"invalid_api_key"</c>,
    /// <c>"not_found"</c>.
    /// </summary>
    public string Kind { get; }

    public AwDocumentsException(string message, string kind, bool isTransient)
        : base(message)
    {
        Kind = kind;
        IsTransient = isTransient;
    }

    public AwDocumentsException(string message, string kind, bool isTransient, Exception inner)
        : base(message, inner)
    {
        Kind = kind;
        IsTransient = isTransient;
    }
}
