namespace Millet.SharedKernel.Infrastructure.Idempotency;

/// <summary>
/// Configuración del middleware + cleanup job de idempotencia (ADR-0020).
/// Bindable a la sección <c>Idempotency:</c> del configuration provider.
/// </summary>
public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    /// <summary>
    /// Si true, el middleware permite paso a todo (endpoints
    /// <c>[RequireIdempotencyKey]</c> NO fallan si falta el header) y el
    /// cleanup job no inicia. Útil para spike de debugging local.
    /// Default: false.
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// Tamaño máximo del request body que se hashea (bytes). Bodies más
    /// grandes hacen 413. Default: 10 MB. Ver ADR-0020 §"Validaciones".
    /// </summary>
    public int MaxRequestBodyBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Tamaño máximo de response cacheada (bytes). Responses mayores
    /// se ejecutan normal pero <c>response_body_truncated=true</c> y los
    /// retries son rechazados con 409 IDEMPOTENCY_RESPONSE_TOO_LARGE.
    /// Default: 1 MB. Ver ADR-0020 §"Tamaño del body cacheable".
    /// </summary>
    public int MaxResponseBodyBytes { get; set; } = 1 * 1024 * 1024;

    /// <summary>
    /// Segundos de <c>Retry-After</c> al devolver 409 IDEMPOTENCY_IN_PROGRESS.
    /// Default: 5. Ver ADR-0020 §"Status processing".
    /// </summary>
    public int RetryAfterSeconds { get; set; } = 5;

    /// <summary>
    /// Retención de keys en estado <c>processing</c> antes del cleanup.
    /// Default: 1 hora. Ver ADR-0020 §"Política de retención".
    /// </summary>
    public TimeSpan ProcessingRetention { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Retención de keys en estado <c>completed</c>/<c>failed</c>.
    /// Default: 24 horas.
    /// </summary>
    public TimeSpan CompletedRetention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Intervalo entre ejecuciones del <c>IdempotencyKeysCleanupJob</c>.
    /// Default: 1 hora.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}
