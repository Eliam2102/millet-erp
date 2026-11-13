using System.Text.Json;
using System.Text.RegularExpressions;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.Api.UnitTests;

/// <summary>
/// I13.6 · el tablero de integraciones mira la cola de errores (dead-letter) de cada suscripción del ERP. Su lista tiene
/// que ser la misma de Azure (servicebus.bicep) y del emulador; si se agrega una suscripción y no se lista aquí, su
/// cola de errores crecería sin que nadie la viera.
/// </summary>
public sealed partial class TableroIntegracionesSuscripcionesTests
{
    private const string SinConsumidor = "sin-consumidor-local";

    [Fact]
    public void Las_suscripciones_del_emulador_estan_en_el_tablero()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(Raiz(), "tools", "servicebus-emulator", "Config.json")));
        var emulador = config.RootElement.GetProperty("UserConfig").GetProperty("Namespaces").EnumerateArray()
            .SelectMany(n => n.GetProperty("Topics").EnumerateArray())
            .SelectMany(t => t.GetProperty("Subscriptions").EnumerateArray()
                .Select(s => $"{t.GetProperty("Name").GetString()}/{s.GetProperty("Name").GetString()}"))
            .Where(s => !s.EndsWith(SinConsumidor, StringComparison.Ordinal));

        Assert.Empty(emulador.Except(Tablero()));
    }

    [Fact]
    public void Las_suscripciones_del_tablero_son_las_de_azure()
    {
        var bicep = File.ReadAllText(Path.Combine(Raiz(), "infra", "modules", "servicebus.bicep"));
        var topics = Topic().Matches(bicep).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        var azure = Suscripcion().Matches(bicep)
            .Select(m => $"{topics[m.Groups[1].Value]}/{m.Groups[2].Value}")
            .Where(s => !s.EndsWith(SinConsumidor, StringComparison.Ordinal));

        Assert.Equal(azure.Order(StringComparer.Ordinal), Tablero().Order(StringComparer.Ordinal));
    }

    private static IEnumerable<string> Tablero() => SuscripcionServiceBus.DelErp.Select(s => $"{s.Topic}/{s.Suscripcion}");

    private static string Raiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "infra", "modules"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("No se encontró la raíz del repositorio (infra/modules).");
    }

    [GeneratedRegex(@"resource (\w+) '[^']+/topics@[^']+' = \{\s*parent: \w+\s*name: '([^']+)'")]
    private static partial Regex Topic();

    [GeneratedRegex(@"resource \w+ '[^']+/topics/subscriptions@[^']+' = \{\s*parent: (\w+)\s*name: '([^']+)'")]
    private static partial Regex Suscripcion();
}
