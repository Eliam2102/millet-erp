using Millet.Integraciones.Aw.Domain.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Integraciones.Aw.Domain;

/// <summary>
/// Un intento individual de drop al servicio on-prem. Hijo de
/// <see cref="EntidadExterna"/>. Múltiples filas por entidad si hay
/// reintentos (cada uno con su <see cref="AttemptNumber"/> incrementado).
///
/// <para>
/// Inmutable después de finalizar — los métodos
/// <see cref="FinalizarExito"/> y <see cref="FinalizarFallo"/> solo se
/// pueden llamar una vez desde estado <see cref="EstadoEnvio.Started"/>.
/// </para>
/// </summary>
public sealed class Envio : BaseEntity
{
    public Guid EntidadExternaId { get; private set; }
    public short AttemptNumber { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public EstadoEnvio Status { get; private set; }
    public string DropServiceUrl { get; private set; } = string.Empty;
    public int? BytesSent { get; private set; }
    public short? HttpStatusCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? ErrorKind { get; private set; }
    public int? DurationMs { get; private set; }

    /// <summary>
    /// Filename del EDI enviado al drop service en este intento (header
    /// <c>X-Filename</c>). Lo usa el <c>AwLateReconciliationWorker</c> para
    /// matchear entradas de <c>Processed\</c> contra el <see cref="EntidadExterna"/>
    /// correspondiente cuando A+W procesa el EDI después del timeout del
    /// drop service. Nullable para tolerar registros históricos previos al
    /// wireado del <c>Envio</c>.
    /// </summary>
    public string? Filename { get; private set; }

    private Envio() { } // EF Core

    private Envio(
        Guid id,
        Guid entidadExternaId,
        short attemptNumber,
        DateTimeOffset startedAt,
        string dropServiceUrl,
        string filename) : base(id)
    {
        EntidadExternaId = entidadExternaId;
        AttemptNumber = attemptNumber;
        StartedAt = startedAt;
        DropServiceUrl = dropServiceUrl;
        Filename = filename;
        Status = EstadoEnvio.Started;
    }

    /// <summary>
    /// Factory: crea un <see cref="Envio"/> en estado
    /// <see cref="EstadoEnvio.Started"/>. El <see cref="EntidadExterna"/>
    /// raíz lo expone (no es constructor público) para preservar la
    /// invariante del agregado.
    /// </summary>
    public static Envio Empezar(
        EntidadExterna entidad,
        short attemptNumber,
        DateTimeOffset startedAt,
        string dropServiceUrl,
        string filename)
    {
        ArgumentNullException.ThrowIfNull(entidad);
        if (attemptNumber < 1)
            throw new ArgumentException("AttemptNumber debe ser >= 1.", nameof(attemptNumber));
        if (string.IsNullOrWhiteSpace(dropServiceUrl))
            throw new ArgumentException("DropServiceUrl es requerida.", nameof(dropServiceUrl));
        if (string.IsNullOrWhiteSpace(filename))
            throw new ArgumentException("Filename es requerido.", nameof(filename));

        return new Envio(
            Guid.CreateVersion7(),
            entidad.Id,
            attemptNumber,
            startedAt,
            dropServiceUrl,
            filename);
    }

    public void FinalizarExito(DateTimeOffset finishedAt, int bytesSent, short httpStatusCode, int durationMs)
    {
        if (Status is not EstadoEnvio.Started)
        {
            throw new InvalidStateTransitionForEnvioException(Status, nameof(FinalizarExito));
        }
        Status = EstadoEnvio.Success;
        FinishedAt = finishedAt;
        BytesSent = bytesSent;
        HttpStatusCode = httpStatusCode;
        DurationMs = durationMs;
    }

    public void FinalizarFallo(
        DateTimeOffset finishedAt,
        EstadoEnvio resultado,
        string errorMessage,
        string errorKind,
        short? httpStatusCode,
        int durationMs)
    {
        if (Status is not EstadoEnvio.Started)
        {
            throw new InvalidStateTransitionForEnvioException(Status, nameof(FinalizarFallo));
        }
        if (resultado is not EstadoEnvio.Failed and not EstadoEnvio.Timeout)
        {
            throw new ArgumentException(
                $"FinalizarFallo solo acepta resultado Failed o Timeout, recibió {resultado}.",
                nameof(resultado));
        }
        Status = resultado;
        FinishedAt = finishedAt;
        ErrorMessage = errorMessage;
        ErrorKind = errorKind;
        HttpStatusCode = httpStatusCode;
        DurationMs = durationMs;
    }
}

/// <summary>
/// Excepción específica para transiciones inválidas en <see cref="Envio"/>.
/// Análoga a <see cref="InvalidStateTransitionException"/> de
/// <see cref="EntidadExterna"/> pero con tipo de estado distinto.
/// </summary>
public sealed class InvalidStateTransitionForEnvioException : SharedKernel.Domain.Exceptions.DomainException
{
    public EstadoEnvio EstadoActual { get; }
    public string TransicionIntentada { get; }

    public override string Code => "AW_ENVIO_INVALID_STATE_TRANSITION";

    public InvalidStateTransitionForEnvioException(EstadoEnvio estadoActual, string transicionIntentada)
        : base($"No se puede ejecutar '{transicionIntentada}' en un Envio con estado '{estadoActual}'. " +
               "Cada Envio finaliza una sola vez (Started → Success/Failed/Timeout).")
    {
        EstadoActual = estadoActual;
        TransicionIntentada = transicionIntentada;
    }
}
