namespace Millet.Tesoreria.Application.Common;

/// <summary>
/// Respuesta paginada offset-based para las bandejas del módulo Tesorería.
/// Misma forma que la usada por Compras/CxP/CxC, replicada localmente para
/// no agregar acoplamiento entre módulos en una primitiva trivial.
/// </summary>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Offset,
    int Limit,
    int Total);
