namespace Millet.Integraciones.Aw.Application.Ports;

/// <summary>
/// Falla en el HTTP drop al servicio on-prem. <see cref="IsTransient"/>
/// distingue entre fallas reintentables (network, 5xx, timeout) y
/// permanentes (4xx auth/validation) — el <c>AwDropWorker</c> usa esa
/// clasificación para decidir Abandon vs DeadLetter del mensaje de
/// Service Bus.
/// </summary>
public sealed class AwDropException : Exception
{
    /// <summary>
    /// True si el caller puede reintentar (Service Bus reintentará el
    /// mensaje). False para errores permanentes — el mensaje va a
    /// dead-letter sin reintentos adicionales.
    /// </summary>
    public bool IsTransient { get; }

    /// <summary>
    /// Categoría corta del error para tagging en métricas/logs (ej.
    /// "http_5xx", "http_401", "timeout", "network", "auth").
    /// </summary>
    public string Kind { get; }

    public AwDropException(string message, string kind, bool isTransient)
        : base(message)
    {
        Kind = kind;
        IsTransient = isTransient;
    }

    public AwDropException(string message, string kind, bool isTransient, Exception inner)
        : base(message, inner)
    {
        Kind = kind;
        IsTransient = isTransient;
    }
}
