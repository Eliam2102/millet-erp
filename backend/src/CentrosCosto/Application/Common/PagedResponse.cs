namespace Millet.CentrosCosto.Application.Common;

/// <summary>
/// Respuesta paginada del módulo (CECO-PR3) — mismo shape que los paged de
/// Almacén/CxC (cada módulo define el suyo; no hay uno compartido en
/// SharedKernel).
/// </summary>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Offset,
    int Limit);
