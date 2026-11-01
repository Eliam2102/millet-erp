namespace Millet.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// Funciones SQL nativas de PostgreSQL expuestas a LINQ vía
/// <c>HasDbFunction</c>. Estos métodos NO se ejecutan en .NET: son
/// marcadores que EF Core traduce a la función SQL correspondiente
/// cuando aparecen dentro de un árbol de expresión (<c>Where</c>,
/// <c>Select</c>, etc.). Invocarlos fuera de una query lanza.
///
/// <para>
/// El mapeo se declara en el <c>OnModelCreating</c> del DbContext que
/// las use (primer caso: <c>CompartidoDbContext</c>, ADR-0045). Otros
/// módulos que necesiten la misma función la mapean igual en su propio
/// contexto reutilizando este método.
/// </para>
/// </summary>
public static class PostgresFunctions
{
    /// <summary>
    /// Mapa de folding de acentos para búsquedas de texto insensibles a
    /// acentos (ADR-0045). Fuente única reutilizada por los endpoints de
    /// catálogos (selectores) y las queries admin de Datos Maestros. Se
    /// aplica <c>lower()</c> ANTES del translate, por eso el mapa solo lista
    /// minúsculas; el folding <c>ñ→n</c> es intencional ("munoz" encuentra
    /// "Muñoz").
    /// </summary>
    public const string AcentosOrigen = "áéíóúüñ";
    public const string AcentosDestino = "aeiouun";

    /// <summary>
    /// Mapea el built-in <c>pg_catalog.translate(text, from, to)</c>:
    /// reemplaza cada carácter de <paramref name="input"/> que aparezca
    /// en <paramref name="from"/> por el carácter en la misma posición
    /// de <paramref name="to"/>. Built-in de PostgreSQL — NO requiere la
    /// extensión <c>unaccent</c> ni allow-list de infraestructura
    /// (ADR-0045). Se usa para folding de acentos en búsquedas de texto
    /// insensibles a acentos.
    /// </summary>
    public static string Translate(string input, string from, string to)
        => throw new InvalidOperationException(
            "PostgresFunctions.Translate solo es válido dentro de una query " +
            "traducida por EF Core (mapeado vía HasDbFunction).");

    /// <summary>
    /// Mapea el built-in <c>pg_catalog.regexp_replace(text, pattern, replacement)</c>.
    /// Lo usa <see cref="OrdenClaveNatural"/> para el orden natural de claves.
    /// </summary>
    public static string RegexpReplace(string input, string pattern, string replacement)
        => throw new InvalidOperationException(
            "PostgresFunctions.RegexpReplace solo es válido dentro de una query " +
            "traducida por EF Core (mapeado vía HasDbFunction).");
}

/// <summary>
/// Orden "natural" de claves tipo <c>AW-1, AW-10, AW-1000</c> (el orden de
/// texto daría AW-1, AW-10, AW-1000, AW-2). Se ordena por prefijo no
/// numérico, luego por longitud (mismo prefijo: más dígitos = número mayor)
/// y al final por texto.
/// <para>
/// ponytail: asume el sufijo numérico al final de la clave; una clave con
/// ceros a la izquierda (<c>AW-01</c>) queda tras <c>AW-1</c>. Si hiciera falta
/// orden natural completo: colación ICU <c>numeric</c> vía migración.
/// </para>
/// </summary>
public static class OrdenClaveNatural
{
    private static readonly System.Reflection.MethodInfo RegexpReplace = typeof(PostgresFunctions).GetMethod(
        nameof(PostgresFunctions.RegexpReplace), new[] { typeof(string), typeof(string), typeof(string) })!;

    private static System.Linq.Expressions.Expression<Func<T, string>> Prefijo<T>(
        System.Linq.Expressions.Expression<Func<T, string>> clave) =>
        System.Linq.Expressions.Expression.Lambda<Func<T, string>>(
            System.Linq.Expressions.Expression.Call(
                RegexpReplace, clave.Body,
                System.Linq.Expressions.Expression.Constant(@"\d+$"),
                System.Linq.Expressions.Expression.Constant(string.Empty)),
            clave.Parameters);

    private static System.Linq.Expressions.Expression<Func<T, int>> Longitud<T>(
        System.Linq.Expressions.Expression<Func<T, string>> clave) =>
        System.Linq.Expressions.Expression.Lambda<Func<T, int>>(
            System.Linq.Expressions.Expression.Property(clave.Body, nameof(string.Length)),
            clave.Parameters);

    public static IOrderedQueryable<T> OrdenarPorClaveNatural<T>(
        this IQueryable<T> q, System.Linq.Expressions.Expression<Func<T, string>> clave) =>
        q.OrderBy(Prefijo(clave)).ThenBy(Longitud(clave)).ThenBy(clave);

    public static IOrderedQueryable<T> LuegoPorClaveNatural<T>(
        this IOrderedQueryable<T> q, System.Linq.Expressions.Expression<Func<T, string>> clave) =>
        q.ThenBy(Prefijo(clave)).ThenBy(Longitud(clave)).ThenBy(clave);
}
