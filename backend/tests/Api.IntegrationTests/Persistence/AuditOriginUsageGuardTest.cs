namespace Millet.Api.IntegrationTests.Persistence;

/// <summary>
/// Test estructural que falla el build si <c>IAuditOriginContext.SetOrigin()</c>
/// se usa fuera de procesos en background reales. A diferencia de
/// <see cref="BypassUsageGuardTest"/> (que también permite Adapters/Resolvers/
/// admin de Identidad, código que corre DENTRO de un request HTTP con un
/// usuario real), <c>SetOrigin</c> solo tiene sentido cuando no hay usuario que
/// atribuir — restringido a <c>Workers/</c>, <c>Seed/</c>, <c>*Seed.cs</c> y
/// <c>Bootstrap*HostedService.cs</c> (los mismos actores "sin HTTP" del
/// F1-ADM-03, propuesta de atribución de origen).
/// </summary>
public class AuditOriginUsageGuardTest
{
    [Fact]
    public void Should_UseSetOrigin_OnlyInBackgroundProcesses()
    {
        var backendRoot = LocateBackendRoot();
        var srcRoot = Path.Combine(backendRoot, "src");

        var violations = new List<(string File, int Line, string Content)>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            // El propio archivo que define SetOrigin() y su interfaz están exentos.
            if (fileName is "AuditOriginContext.cs" or "IAuditOriginContext.cs")
                continue;

            var relative = Path.GetRelativePath(backendRoot, file);
            if (IsWhitelisted(relative, fileName))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(".SetOrigin(", StringComparison.Ordinal))
                    continue;

                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("///", StringComparison.Ordinal))
                    continue;

                violations.Add((relative, i + 1, lines[i].Trim()));
            }
        }

        violations.Should().BeEmpty(
            $"IAuditOriginContext.SetOrigin() solo se permite en Workers/, Seed/, o " +
            $"Bootstrap*HostedService.cs (procesos en background sin HttpContext). " +
            $"Encontradas {violations.Count} violación(es):\n" +
            string.Join("\n", violations.Select(v => $"  {v.File}:{v.Line}  {v.Content}")));
    }

    private static bool IsWhitelisted(string relativePath, string fileName)
    {
        var sep = Path.DirectorySeparatorChar;

        if (relativePath.Contains($"{sep}Workers{sep}", StringComparison.Ordinal)
            || relativePath.Contains($"{sep}Seed{sep}", StringComparison.Ordinal)
            || relativePath.EndsWith("Seed.cs", StringComparison.Ordinal))
        {
            return true;
        }

        if (fileName.StartsWith("Bootstrap", StringComparison.Ordinal)
            && fileName.EndsWith("HostedService.cs", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static string LocateBackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.Name.Equals("backend", StringComparison.OrdinalIgnoreCase))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"No se encontró la carpeta 'backend' desde {AppContext.BaseDirectory}. " +
            "El test debe correrse desde el repo (no desde un publish aislado).");
    }
}
