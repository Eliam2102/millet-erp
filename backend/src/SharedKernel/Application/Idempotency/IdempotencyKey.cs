namespace Millet.SharedKernel.Application.Idempotency;

/// <summary>
/// Persistencia de un request idempotente: clave (UUID v4 generado por el
/// cliente) + tenant + usuario + hash del body + estado + respuesta cacheada.
/// PK compuesta <c>(empresa_id, usuario_id, key, http_method, path)</c>:
/// aísla keys entre usuarios (ADR-0020) y entre endpoints — la misma key
/// en otra ruta es otra operación y nunca recibe la respuesta de la primera.
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

    /// <summary>
    /// Response body serializado a JSON. Null si <see cref="ResponseBodyTruncated"/>
    /// o si la respuesta original no tenía body (p. ej. 204).
    /// </summary>
    public string? ResponseBody { get; set; }

    /// <summary>
    /// Headers de la respuesta original que un replay debe repetir
    /// (<c>ETag</c>, <c>Location</c>), como objeto JSON. Null si no hubo.
    /// </summary>
    public string? ResponseHeaders { get; set; }

    /// <summary>True si la response excedió el cap o no era JSON (no se cachea; un retry se rechaza).</summary>
    public bool ResponseBodyTruncated { get; set; }

    public Guid CorrelationId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; set; }
}
