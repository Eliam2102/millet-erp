using Microsoft.EntityFrameworkCore;

namespace Millet.SharedKernel.Application;

/// <summary>
/// Una página de un listado con el total real de filas que cumplen el filtro
/// (no el de la página), para que la pantalla pagine contra el servidor.
/// </summary>
public sealed record Pagina<T>(IReadOnlyList<T> Items, int Total, int Offset, int Limit);

/// <summary>
/// Paginación servidor reutilizable para los listados (offset + límite). Un
/// listado que sólo hace <c>Take(200)</c> y filtra en el navegador deja fuera
/// todo lo anterior a las 200 filas más recientes; este helper cuenta y corta
/// en la base de datos.
/// </summary>
public static class PaginacionExtensions
{
    public const int LimitePorOmision = 50;
    public const int LimiteMaximo = 200;

    /// <summary>Offset ≥ 0; límite 0 o negativo = por omisión; tope en <paramref name="maximo"/>.</summary>
    public static (int Offset, int Limit) Normalizar(int offset, int limit, int maximo = LimiteMaximo)
        => (Math.Max(0, offset), Math.Clamp(limit <= 0 ? LimitePorOmision : limit, 1, maximo));

    /// <summary>
    /// Cuenta y devuelve la página. El <paramref name="query"/> debe llegar ya
    /// filtrado y ORDENADO con un criterio estable (sin orden, las páginas se
    /// pueden repetir o saltar filas).
    /// </summary>
    public static async Task<Pagina<T>> ToPaginaAsync<T>(
        this IQueryable<T> query, int offset, int limit, CancellationToken cancellationToken, int maximo = LimiteMaximo)
    {
        var (o, l) = Normalizar(offset, limit, maximo);
        var total = await query.CountAsync(cancellationToken);
        var items = total == 0 || o >= total
            ? new List<T>()
            : await query.Skip(o).Take(l).ToListAsync(cancellationToken);
        return new Pagina<T>(items, total, o, l);
    }

    /// <summary>Patrón <c>%texto%</c> para <c>EF.Functions.ILike</c>, con <c>\</c>, <c>%</c> y <c>_</c> del usuario escapados.</summary>
    public static string PatronContiene(string texto)
        => "%" + texto.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
