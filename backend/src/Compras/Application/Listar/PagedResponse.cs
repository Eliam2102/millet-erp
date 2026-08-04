namespace Millet.Compras.Application.Listar;

/// <summary>
/// Respuesta paginada genérica usada por las bandejas. Offset-based:
/// el cliente envía <c>offset</c> y <c>limit</c>; el servidor devuelve
/// los items y el total para que la UI pinte controles de paginación.
/// </summary>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Offset,
    int Limit,
    int Total);
