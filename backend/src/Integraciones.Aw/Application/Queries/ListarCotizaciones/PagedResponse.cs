namespace Millet.Integraciones.Aw.Application.Queries.ListarCotizaciones;

/// <summary>
/// Respuesta paginada offset-based del módulo. Replica el patrón de
/// <c>Millet.Compras.Application.Listar.PagedResponse&lt;T&gt;</c> sin
/// promover a SharedKernel (decisión PR D — fuera de scope).
/// </summary>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Offset,
    int Limit,
    int Total);
