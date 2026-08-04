namespace Millet.Api.IntegrationTests.Persistence;

/// <summary>
/// Test estructural que falla el build si <c>EmpresaContext.Bypass()</c> se
/// usa fuera de contextos legítimos. La regla base está en ADR-0011 y
/// ADR-0007: el bypass del filtro de empresa nunca debe aparecer en
/// handlers de negocio comunes. ADR-0039 formaliza los contextos
/// cross-tenant legítimos donde sí se permite.
///
/// Whitelist (ver ADR-0039 para el racional de cada categoría):
/// <list type="bullet">
///   <item><c>tests/</c>: lo escanea solo para src/, no se incluye.</item>
///   <item><c>Migrations/</c>, <c>Seed/</c>, <c>*Seed.cs</c>: bypass
///         legítimo cuando se aplica seed o data global (preexistente).</item>
///   <item><c>Workers/</c>: jobs de fondo sin contexto HTTP (preexistente).</item>
///   <item><c>Adapters/</c>, <c>PublicAdapters/</c>: puertos cross-bounded-context
///         que leen catálogos cross-empresa.</item>
///   <item><c>LoginOrchestrator.cs</c>, <c>PermissionLoader.cs</c>,
///         <c>Bootstrap*HostedService.cs</c>: auth y bootstrap globales.</item>
///   <item><c>*Resolver.cs</c> bajo <c>Infrastructure/</c>: resuelven
///         configuración cross-empresa.</item>
///   <item><c>Identidad/Application/Usuarios/*</c> y <c>Identidad/Application/Roles/*</c>:
///         admin de identidad opera cross-empresa por diseño (RBAC es el gate).</item>
///   <item><c>CurrentEmpresaContext.cs</c> e <c>ICurrentEmpresaContext.cs</c>:
///         definen Bypass(), exentos por construcción.</item>
/// </list>
/// </summary>
public class BypassUsageGuardTest
{
    [Fact]
    public void Should_NotUseBypass_OutsideTestsAndMigrationsAndSeed()
    {
        var backendRoot = LocateBackendRoot();
        var srcRoot = Path.Combine(backendRoot, "src");

        var violations = new List<(string File, int Line, string Content)>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            // El propio archivo que define Bypass() y su interfaz están exentos.
            var fileName = Path.GetFileName(file);
            if (fileName is "CurrentEmpresaContext.cs" or "ICurrentEmpresaContext.cs")
                continue;

            var relative = Path.GetRelativePath(backendRoot, file);
            if (IsWhitelisted(relative))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(".Bypass()", StringComparison.Ordinal))
                    continue;

                // Ignorar comentarios de línea (//) y XML doc (///).
                // Las referencias a .Bypass() en docstrings son legítimas
                // (explican el patrón) y no constituyen un uso real.
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                violations.Add((relative, i + 1, lines[i].Trim()));
            }
        }

        violations.Should().BeEmpty(
            $"EmpresaContext.Bypass() solo se permite en tests/, Migrations/, o Seed/. " +
            $"Encontradas {violations.Count} violación(es):\n" +
            string.Join("\n", violations.Select(v => $"  {v.File}:{v.Line}  {v.Content}")));
    }

    private static bool IsWhitelisted(string relativePath)
    {
        var sep = Path.DirectorySeparatorChar;

        // Categorías preexistentes: Migrations/, Seed/, *Seed.cs, Workers/.
        // Migrations y Seed corren con data global. Workers corren en
        // background sin JWT — equivalente a Migrations/Seed para
        // efectos del filtro por empresa.
        if (relativePath.Contains($"{sep}Migrations{sep}", StringComparison.Ordinal)
            || relativePath.Contains($"{sep}Seed{sep}", StringComparison.Ordinal)
            || relativePath.EndsWith("Seed.cs", StringComparison.Ordinal)
            || relativePath.Contains($"{sep}Workers{sep}", StringComparison.Ordinal))
        {
            return true;
        }

        // Categorías nuevas (ADR-0039). El racional de cada una vive
        // en el ADR; aquí solo enforce.
        //
        // 1) Adapters cross-bounded-context: puertos públicos que un
        //    módulo expone a otros leen catálogos cross-empresa sin
        //    contexto HTTP del caller.
        if (relativePath.Contains($"{sep}Adapters{sep}", StringComparison.Ordinal)
            || relativePath.Contains($"{sep}PublicAdapters{sep}", StringComparison.Ordinal))
        {
            return true;
        }

        var fileName = Path.GetFileName(relativePath);

        // 2) Auth y bootstrap globales: operan antes del contexto de
        //    empresa (login) o en arranque (provisión inicial).
        if (fileName is "LoginOrchestrator.cs" or "PermissionLoader.cs")
        {
            return true;
        }
        if (fileName.StartsWith("Bootstrap", StringComparison.Ordinal)
            && fileName.EndsWith("HostedService.cs", StringComparison.Ordinal))
        {
            return true;
        }

        // 3) Resolvers cross-empresa de configuración (PAC, service
        //    principals, etc.) bajo Infrastructure/.
        if (fileName.EndsWith("Resolver.cs", StringComparison.Ordinal)
            && relativePath.Contains($"{sep}Infrastructure{sep}", StringComparison.Ordinal))
        {
            return true;
        }

        // 4) Admin handlers del módulo Identidad. UsuarioEmpresaRol y
        //    entidades relacionadas son cross-empresa por diseño; el
        //    RBAC granular (identidad.*) es el gate efectivo.
        var identidadUsuarios = $"src{sep}Identidad{sep}Application{sep}Usuarios{sep}";
        var identidadRoles = $"src{sep}Identidad{sep}Application{sep}Roles{sep}";
        if (relativePath.Contains(identidadUsuarios, StringComparison.Ordinal)
            || relativePath.Contains(identidadRoles, StringComparison.Ordinal))
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
