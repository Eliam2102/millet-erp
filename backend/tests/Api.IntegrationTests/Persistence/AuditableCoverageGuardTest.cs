using System.Reflection;
using Millet.SharedKernel.Domain;

namespace Millet.Api.IntegrationTests.Persistence;

/// <summary>
/// Test estructural que exige que toda entidad de dominio (clase que deriva
/// de <see cref="BaseEntity"/>, dentro de un namespace <c>*.Domain.*</c>)
/// declare explícitamente <see cref="IAuditable"/> o <see cref="INotAudited"/>.
///
/// Es la Capa 4 de ADR-0008 ("test/linter en CI... falla si una entidad NO
/// declara explícitamente"), nunca construida hasta F1-ADM-03. Su ausencia
/// dejó 70 entidades en 8 módulos auditadas por omisión negativa — sin
/// declaración, sin error de compilación, sin auditoría — incluyendo
/// entidades con impacto fiscal/financiero directo (<c>FacturaProveedor</c>,
/// <c>Comprobante</c>, <c>CuentaBancaria</c>). Ver
/// <c>VidriosMillet-Tareas/F1-ADM-03-auditoria/04-hallazgo-cobertura-iauditable.md</c>
/// (fuera del repo).
///
/// Escanea por reflexión los ensamblados <c>Millet.*.dll</c> compilados
/// (no test-scan de texto, para no perder subclases con la interfaz
/// heredada de una base) en vez de <c>src/</c> línea por línea, porque lo
/// que importa es si el TIPO implementa la interfaz (directa o heredada de
/// una clase base), no si el archivo la menciona.
/// </summary>
public class AuditableCoverageGuardTest
{
    [Fact]
    public void Should_DeclareAuditableOrNotAudited_OnEveryDomainEntity()
    {
        var violations = new List<string>();

        foreach (var type in LoadDomainEntityTypes())
        {
            var isAuditable = typeof(IAuditable).IsAssignableFrom(type);
            var isNotAudited = typeof(INotAudited).IsAssignableFrom(type);

            if (isAuditable && isNotAudited)
            {
                violations.Add($"{type.FullName} implementa IAuditable Y INotAudited a la vez (ambiguo).");
                continue;
            }

            if (!isAuditable && !isNotAudited)
            {
                violations.Add($"{type.FullName} no declara IAuditable ni INotAudited.");
            }
        }

        violations.Should().BeEmpty(
            $"Toda entidad que deriva de BaseEntity debe declarar explícitamente " +
            $"IAuditable o INotAudited (ADR-0008, Capa 4). Encontradas {violations.Count} violación(es):\n" +
            string.Join("\n", violations.OrderBy(v => v)));
    }

    /// <summary>
    /// Carga los ensamblados <c>Millet.*.dll</c> de src/ (excluye proyectos de
    /// test) desde el directorio de salida del test runner, y devuelve las
    /// clases concretas o abstractas que derivan de <see cref="BaseEntity"/>
    /// dentro de un namespace <c>*.Domain*</c>.
    /// </summary>
    internal static IEnumerable<Type> LoadDomainEntityTypes()
    {
        var baseDir = AppContext.BaseDirectory;
        var dllPaths = Directory.EnumerateFiles(baseDir, "Millet.*.dll", SearchOption.TopDirectoryOnly)
            .Where(p => !Path.GetFileNameWithoutExtension(p).EndsWith("Tests", StringComparison.OrdinalIgnoreCase));

        var types = new List<Type>();
        foreach (var path in dllPaths)
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(path);
            }
            catch
            {
                continue;
            }

            Type[] assemblyTypes;
            try
            {
                assemblyTypes = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                assemblyTypes = ex.Types.Where(t => t is not null).Select(t => t!).ToArray();
            }

            types.AddRange(assemblyTypes);
        }

        return types
            .Where(t => t is { IsClass: true, Namespace: not null })
            .Where(t => t.Namespace!.Contains(".Domain", StringComparison.Ordinal))
            .Where(t => t != typeof(BaseEntity) && typeof(BaseEntity).IsAssignableFrom(t))
            .Distinct();
    }
}
