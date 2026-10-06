namespace Millet.Contabilidad.Application;

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, int Limit);
