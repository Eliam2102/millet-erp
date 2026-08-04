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
}
