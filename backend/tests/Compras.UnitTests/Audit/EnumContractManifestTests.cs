using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Millet.Catalogos.Domain;
using Millet.Compras.Application.Historico;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.UnitTests.Audit;

/// <summary>
/// Guard de contrato de los enums espejo de Compras (Opción A del
/// análisis del bug HistoricoTipo). El frontend replica a mano varios
/// enums del backend como objetos <c>as const</c> con valores numéricos;
/// si un valor se desincroniza (como pasó con <see cref="HistoricoTipo"/>,
/// que omitía el sentinel <c>Cambio=0</c> y corría +1 todo el mirror),
/// el histórico se rotula mal sin que ningún test simbólico lo detecte.
///
/// <para>
/// Este test refleja sobre los enums listados y los compara — por VALOR
/// NUMÉRICO — contra el manifiesto commiteado
/// <c>docs/api/compras-enums.contract.json</c>. El mismo manifiesto lo
/// consume el guard del frontend (<c>enum-contract.test.ts</c>), de modo
/// que un drift de cualquiera de los dos lados rompe el build.
/// </para>
///
/// <para>
/// <b>Regeneración del fixture</b> (no editar a mano): correr el test con
/// la variable de entorno <c>UPDATE_ENUM_CONTRACT=1</c> reescribe el JSON
/// desde la reflexión y commitearlo. El toggle NO se activa en CI (CI corre
/// sin esa variable, así que el test asserta).
/// <code>
/// # PowerShell:  $env:UPDATE_ENUM_CONTRACT=1; dotnet test --filter EnumContractManifestTests
/// # bash:        UPDATE_ENUM_CONTRACT=1 dotnet test --filter EnumContractManifestTests
/// </code>
/// </para>
///
/// <para>
/// <b>Limitación</b>: el guard cubre solo los enums LISTADOS en
/// <see cref="EnumsEspejo"/>. Un enum espejo nuevo debe registrarse aquí,
/// en el manifiesto y en el guard del frontend. El fix de fondo (codegen
/// desde OpenAPI) es ADR-0017 — follow-up, fuera de este PR.
/// </para>
/// </summary>
public class EnumContractManifestTests
{
    /// <summary>
    /// Los 16 enums de Compras que el frontend espeja a mano. La clave es
    /// el nombre con el que el manifiesto y el objeto espejo del frontend
    /// los identifican (= nombre simple del tipo C#).
    /// </summary>
    private static readonly IReadOnlyList<Type> EnumsEspejo =
    [
        typeof(HistoricoTipo),
        typeof(EstadoRequisicion),
        typeof(SituacionSurtido),
        typeof(EstadoOrdenCompra),
        typeof(NivelAutorizacion),
        typeof(Clasificacion),
        typeof(Prioridad),
        typeof(MotivoRechazoAplicaA),
        typeof(RolAprobador),
        typeof(SubEstadoRecepcion),
        typeof(SubEstadoFacturacion),
        typeof(SubEstadoPago),
        typeof(DescuentoTipo),
        typeof(ResultadoAutorizacionOc),
        typeof(Naturaleza),
        typeof(EstatusCatalogo),
    ];

    [Fact]
    public void Manifiesto_CoincideConLosEnumsDelBackend()
    {
        var actual = ReflejarEnums();
        var path = RutaManifiesto();

        if (string.Equals(
                Environment.GetEnvironmentVariable("UPDATE_ENUM_CONTRACT"),
                "1",
                StringComparison.Ordinal))
        {
            EscribirManifiesto(path, actual);
        }

        File.Exists(path).Should().BeTrue(
            $"el manifiesto debe existir en {path}. Regenéralo con UPDATE_ENUM_CONTRACT=1.");

        var esperado = LeerManifiesto(path);

        // Comparación por valor numérico, bidireccional: mismos enums,
        // mismos miembros, mismos enteros.
        actual.Should().BeEquivalentTo(esperado,
            "el manifiesto commiteado debe coincidir con los enums del backend; "
            + "si cambiaste un enum, regenera con UPDATE_ENUM_CONTRACT=1 y commitea el JSON.");
    }

    /// <summary>Reflexión: tipo → { nombre miembro → valor entero }.</summary>
    private static SortedDictionary<string, SortedDictionary<string, int>> ReflejarEnums()
    {
        var result = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        foreach (var tipo in EnumsEspejo)
        {
            var miembros = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var nombre in Enum.GetNames(tipo))
            {
                var valor = Convert.ToInt32(Enum.Parse(tipo, nombre));
                miembros[nombre] = valor;
            }

            result[tipo.Name] = miembros;
        }

        return result;
    }

    private static SortedDictionary<string, SortedDictionary<string, int>> LeerManifiesto(string path)
    {
        var json = File.ReadAllText(path);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(json)
            ?? throw new InvalidOperationException($"Manifiesto vacío o inválido: {path}");

        var result = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        foreach (var (tipo, miembros) in parsed)
        {
            result[tipo] = new SortedDictionary<string, int>(miembros, StringComparer.Ordinal);
        }

        return result;
    }

    /// <summary>
    /// Serializa el manifiesto determinístico: enums ordenados alfabéticamente,
    /// miembros por valor numérico ascendente (diffs estables). Indent 2 +
    /// newline final, alineado con la convención de <c>openapi-v1.json</c>.
    /// </summary>
    private static void EscribirManifiesto(
        string path,
        SortedDictionary<string, SortedDictionary<string, int>> enums)
    {
        var root = new JsonObject();
        foreach (var (tipo, miembros) in enums)
        {
            var obj = new JsonObject();
            foreach (var (nombre, valor) in miembros.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
            {
                obj[nombre] = valor;
            }

            root[tipo] = obj;
        }

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
    }

    /// <summary>
    /// Sube desde <c>AppContext.BaseDirectory</c> hasta la raíz del repo
    /// (el directorio que contiene <c>docs/api</c>) y devuelve la ruta del
    /// manifiesto. Robusto ante cambios en la profundidad de bin/.
    /// </summary>
    private static string RutaManifiesto()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs", "api")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"No se encontró la raíz del repo (carpeta con 'docs/api') desde {AppContext.BaseDirectory}.");
        }

        return Path.Combine(dir.FullName, "docs", "api", "compras-enums.contract.json");
    }
}
