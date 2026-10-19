namespace Millet.CuentasPorPagar.Application.Common;

/// <summary>Patrón <c>ILIKE '%texto%'</c> con los comodines del texto del usuario escapados (escape explícito: la sobrecarga de 2 argumentos de ILike no lo garantiza).</summary>
public static class LikePatron
{
    public const string Escape = "\\";

    public static string Contiene(string q) =>
        "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
