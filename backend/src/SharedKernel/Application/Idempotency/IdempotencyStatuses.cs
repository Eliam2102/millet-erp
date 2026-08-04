namespace Millet.SharedKernel.Application.Idempotency;

/// <summary>
/// Estados válidos para <see cref="IdempotencyKey.Status"/>. Persistidos
/// como text en PG (CHECK constraint en la migración). Ver ADR-0020.
/// </summary>
public static class IdempotencyStatuses
{
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
