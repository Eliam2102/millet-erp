using System.Text.RegularExpressions;

namespace Millet.Compras.UnitTests.Audit;

/// <summary>
/// Audit F10-PR1 — verifica que cada mutación HTTP en
/// <c>OrdenesCompraEndpoints.cs</c> está decorada con
/// <c>RequireIdempotencyKeyAttribute</c>, salvo las exenciones
/// explícitas documentadas en
/// <see cref="ExentosDocumentados"/>. Si alguien agrega un nuevo
/// endpoint de mutación sin Idempotency-Key, el test falla y le
/// recuerda decorarlo o agregarlo a la lista de exenciones con
/// justificación.
///
/// <para>
/// Implementación: escaneo de la fuente .cs (no usa reflection ni
/// HTTP). Esto evita necesidad de un host WebApplication y corre en
/// el filtro <c>~UnitTests</c> de CI.
/// </para>
/// </summary>
public class IdempotencyCoverageOcTests
{
    /// <summary>
    /// Endpoints exentos por decisión de diseño. Cada uno tiene un
    /// comentario en el código fuente explicando por qué no requiere
    /// <c>Idempotency-Key</c>. Llave del dict: marker único en la
    /// declaración del MapXxx (idealmente el path del endpoint).
    /// </summary>
    private static readonly Dictionary<string, string> ExentosDocumentados = new()
    {
        // Eliminar línea solo en Borrador/Rechazada — sin impacto fiscal.
        ["MapDelete(\"/{id:guid}/lineas/{lineaId:guid}\""] = "eliminar línea solo en Borrador/Rechazada",
        // Actualizar texto-adicional — anotación operativa sin transición.
        ["MapPatch(\"/{id:guid}/lineas/{lineaId:guid}/texto-adicional\""] = "texto adicional sin impacto fiscal",
        // Remover adjunto — solo en Borrador.
        ["MapDelete(\"/{id:guid}/adjuntos/{adjuntoId:guid}\""] = "remover adjunto solo en Borrador",
    };

    [Fact]
    public void CadaMutacionTieneIdempotencyKeyODocumentada()
    {
        var source = LeerOrdenesCompraEndpoints();
        var mutaciones = Regex.Matches(source, @"group\.(MapPost|MapPatch|MapDelete)\([^,]+,")
            .Select(m => m.Value)
            .ToList();

        Assert.NotEmpty(mutaciones);

        var faltantes = new List<string>();
        foreach (var mut in mutaciones)
        {
            var sigla = ExtraerSigla(mut);

            // 1) ¿exenta documentada?
            if (ExentosDocumentados.Keys.Any(k => mut.Contains(k, StringComparison.Ordinal)))
                continue;

            // 2) ¿tiene RequireIdempotencyKey en su bloque (hasta el próximo group.Map*)?
            var idx = source.IndexOf(mut, StringComparison.Ordinal);
            var siguiente = NextMutationIndex(source, idx + mut.Length);
            var bloque = source.Substring(idx, siguiente - idx);
            if (!bloque.Contains("RequireIdempotencyKeyAttribute", StringComparison.Ordinal))
            {
                faltantes.Add(sigla);
            }
        }

        Assert.Empty(faltantes);
    }

    [Fact]
    public void ExentosDeclaradosEstanEnElSourceFile()
    {
        var source = LeerOrdenesCompraEndpoints();
        foreach (var (marker, _) in ExentosDocumentados)
        {
            Assert.Contains(marker, source, StringComparison.Ordinal);
        }
    }

    private static int NextMutationIndex(string source, int from)
    {
        var next = Regex.Match(source.Substring(from), @"group\.(MapGet|MapPost|MapPatch|MapDelete)\(");
        return next.Success ? from + next.Index : source.Length;
    }

    private static string ExtraerSigla(string mutMatch)
    {
        var path = Regex.Match(mutMatch, @"""(/[^""]*)""");
        return path.Success ? path.Groups[1].Value : mutMatch;
    }

    private static string LeerOrdenesCompraEndpoints()
    {
        // Subir desde tests/Compras.UnitTests/bin/Debug/net9.0/ hasta
        // raíz del repo y bajar al archivo. AppContext.BaseDirectory
        // apunta a la carpeta bin.
        var baseDir = AppContext.BaseDirectory;
        var path = Path.GetFullPath(Path.Combine(
            baseDir,
            "..", "..", "..", "..", "..",
            "src", "Api", "Endpoints", "Compras", "Oc", "OrdenesCompraEndpoints.cs"));

        Assert.True(File.Exists(path), $"No se encontró el archivo: {path}");
        return File.ReadAllText(path);
    }
}
