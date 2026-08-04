namespace Millet.SharedKernel.Application.Idempotency;

/// <summary>
/// Persistencia de un request idempotente: clave (UUID v4 generado por el
/// cliente) + tenant + usuario + hash del body + estado + respuesta cacheada.
/// PK compuesta <c>(empresa_id, usuario_id, key)</c> aísla keys entre
/// usuarios para evitar fugas cross-user (ADR-0020).
///
/// <para>
/// No extiende <c>BaseEntity</c>: las idempotency keys no son entidades de
/// negocio, no llevan concurrency token, no se auditan, y se purgan por
/// retención. Modelo plano con campos init-only.
/// </para>
/// </summary>
public sealed class IdempotencyKey
{
    public string Key { get; init; } = string.Empty;

    public Guid EmpresaId { get; init; }

    public Guid UsuarioId { get; init; }

    public string HttpMethod { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    /// <summary>SHA-256 hex (lower) del body del request original.</summary>
    public string RequestBodyHash { get; init; } = string.Empty;

    /// <summary>Uno de <see cref="IdempotencyStatuses"/>.</summary>
    public string Status { get; set; } = IdempotencyStatuses.Processing;

    public int? ResponseStatusCode { get; set; }

    /// <summary>Response body serializado a JSON. Null si <see cref="ResponseBodyTruncated"/>.</summary>
    public string? ResponseBody { get; set; }

    /// <summary>True si la response excedió el cap (no se cachea, retries re-ejecutan).</summary>
    public bool ResponseBodyTruncated { get; set; }

    public Guid CorrelationId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; set; }
}
