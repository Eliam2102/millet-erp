namespace Millet.Integraciones.Aw.Application.Ports;

/// <summary>
/// Falla leyendo SQL Server on-prem (vía Hybrid Connection).
/// <see cref="IsTransient"/> distingue entre HC down / SQL down / timeout
/// (reintentable, AwCorrelationWorker incrementa _consecutiveFailures y
/// sigue) vs auth (login failed, password incorrecto — requiere
/// intervención humana, no recuperable por reintentos).
/// </summary>
public sealed class AwReaderException : Exception
{
    public bool IsTransient { get; }

    /// <summary>Categoría: "connection", "auth", "timeout", "query".</summary>
    public string Kind { get; }

    public AwReaderException(string message, string kind, bool isTransient)
        : base(message)
    {
        Kind = kind;
        IsTransient = isTransient;
    }

    public AwReaderException(string message, string kind, bool isTransient, Exception inner)
        : base(message, inner)
    {
        Kind = kind;
        IsTransient = isTransient;
    }
}
