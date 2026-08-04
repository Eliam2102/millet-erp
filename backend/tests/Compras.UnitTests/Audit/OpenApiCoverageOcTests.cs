using System.Text.RegularExpressions;

namespace Millet.Compras.UnitTests.Audit;

/// <summary>
/// Audit F10-PR2 — verifica que cada endpoint HTTP en
/// <c>OrdenesCompraEndpoints.cs</c> tiene <c>.WithName(...)</c>,
/// <c>.WithSummary(...)</c> y al menos una declaración
/// <c>.Produces(...)</c>. Esto garantiza un OpenAPI generado completo
/// que el frontend pueda consumir sin sorpresas.
///
/// <para>
/// Cuando alguien agregue un endpoint sin estas anotaciones, el test
/// falla y le pide completarlas — evita la "deuda de doc" silenciosa.
/// </para>
/// </summary>
public class OpenApiCoverageOcTests
{
    [Fact]
    public void CadaEndpointTieneWithNameSummaryYProduces()
    {
        var source = LeerOrdenesCompraEndpoints();
        var endpoints = Regex.Matches(source, @"group\.(MapGet|MapPost|MapPatch|MapDelete)\(""[^""]+""")
            .Select(m => new
            {
                Match = m.Value,
                Index = m.Index,
            })
            .ToList();

        Assert.NotEmpty(endpoints);

        var faltantes = new List<string>();
        for (var i = 0; i < endpoints.Count; i++)
        {
            var inicio = endpoints[i].Index;
            var fin = i + 1 < endpoints.Count ? endpoints[i + 1].Index : source.Length;
            var bloque = source.Substring(inicio, fin - inicio);

            var missing = new List<string>();
            if (!bloque.Contains(".WithName(", StringComparison.Ordinal)) missing.Add(".WithName");
            if (!bloque.Contains(".WithSummary(", StringComparison.Ordinal)) missing.Add(".WithSummary");
            if (!bloque.Contains(".Produces", StringComparison.Ordinal)) missing.Add(".Produces");

            if (missing.Count > 0)
            {
                var path = ExtraerPath(endpoints[i].Match);
                faltantes.Add($"{path} -> faltan: {string.Join(", ", missing)}");
            }
        }

        Assert.Empty(faltantes);
    }

    private static string ExtraerPath(string match)
    {
        var path = Regex.Match(match, @"""(/[^""]*)""");
        return path.Success ? path.Groups[1].Value : match;
    }

    private static string LeerOrdenesCompraEndpoints()
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.GetFullPath(Path.Combine(
            baseDir,
            "..", "..", "..", "..", "..",
            "src", "Api", "Endpoints", "Compras", "Oc", "OrdenesCompraEndpoints.cs"));

        Assert.True(File.Exists(path), $"No se encontró el archivo: {path}");
        return File.ReadAllText(path);
    }
}
