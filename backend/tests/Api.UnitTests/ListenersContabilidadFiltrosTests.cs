using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Contabilidad.Infrastructure.Workers;

namespace Millet.Api.UnitTests;

/// <summary>
/// C1.5 · cada listener de Contabilidad ignora los eventos informativos con su lista de tipos contables; esa lista tiene que ser la
/// misma que el filtro de su suscripción en Azure (servicebus.bicep) y en el emulador, o la entrega local y Service Bus divergen.
/// </summary>
public sealed partial class ListenersContabilidadFiltrosTests
{
    public static TheoryData<string> Suscripciones => new()
    {
        FacturacionContabilidadListenerWorker.SubscriptionName,
        CuentasPorPagarContabilidadListenerWorker.SubscriptionName,
        TesoreriaContabilidadListenerWorker.SubscriptionName,
        AlmacenContabilidadListenerWorker.SubscriptionName,
    };

    [Theory]
    [MemberData(nameof(Suscripciones))]
    public void La_lista_del_listener_es_la_del_filtro_de_su_suscripcion_en_bicep_y_en_el_emulador(string suscripcion)
    {
        var listener = Listener(suscripcion);

        Assert.Equal(listener.TiposContables.Order(StringComparer.Ordinal), FiltroBicep(suscripcion).Order(StringComparer.Ordinal));
        Assert.Equal(listener.TiposContables.Order(StringComparer.Ordinal), FiltroEmulador(suscripcion).Order(StringComparer.Ordinal));
    }

    private static ContabilidadEventListenerWorker Listener(string suscripcion) => suscripcion switch
    {
        FacturacionContabilidadListenerWorker.SubscriptionName =>
            new FacturacionContabilidadListenerWorker(null, null!, NullLogger<FacturacionContabilidadListenerWorker>.Instance),
        CuentasPorPagarContabilidadListenerWorker.SubscriptionName =>
            new CuentasPorPagarContabilidadListenerWorker(null, null!, NullLogger<CuentasPorPagarContabilidadListenerWorker>.Instance),
        TesoreriaContabilidadListenerWorker.SubscriptionName =>
            new TesoreriaContabilidadListenerWorker(null, null!, NullLogger<TesoreriaContabilidadListenerWorker>.Instance),
        _ => new AlmacenContabilidadListenerWorker(null, null!, NullLogger<AlmacenContabilidadListenerWorker>.Instance),
    };

    /// <summary>Tipos del <c>sqlExpression</c> de la regla cuyo padre es la suscripción con ese nombre.</summary>
    private static string[] FiltroBicep(string suscripcion)
    {
        var bicep = File.ReadAllText(Path.Combine(Raiz(), "infra", "modules", "servicebus.bicep"));
        var recurso = Regex.Match(bicep, $@"resource (\w+) '[^']+/subscriptions@[^']+' = \{{\s*parent: \w+\s*name: '{Regex.Escape(suscripcion)}'");
        Assert.True(recurso.Success, $"No está la suscripción {suscripcion} en servicebus.bicep.");
        var regla = Regex.Match(bicep, $@"parent: {recurso.Groups[1].Value}\s[\s\S]*?sqlExpression: '([^\n]*)'");
        Assert.True(regla.Success, $"La suscripción {suscripcion} no tiene filtro en servicebus.bicep.");
        return Tipos(regla.Groups[1].Value);
    }

    private static string[] FiltroEmulador(string suscripcion)
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(Raiz(), "tools", "servicebus-emulator", "Config.json")));
        var sub = config.RootElement.GetProperty("UserConfig").GetProperty("Namespaces").EnumerateArray()
            .SelectMany(n => n.GetProperty("Topics").EnumerateArray())
            .SelectMany(t => t.GetProperty("Subscriptions").EnumerateArray())
            .Single(s => s.GetProperty("Name").GetString() == suscripcion);
        var expresion = sub.GetProperty("Rules").EnumerateArray().Single()
            .GetProperty("Properties").GetProperty("SqlFilter").GetProperty("SqlExpression").GetString()!;
        return Tipos(expresion);
    }

    private static string[] Tipos(string expresion) => [.. TipoEvento().Matches(expresion).Select(m => m.Value)];

    private static string Raiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "infra", "modules"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("No se encontró la raíz del repositorio (infra/modules).");
    }

    [GeneratedRegex(@"[a-z_]+\.[a-z_.\-]+\.v\d+")]
    private static partial Regex TipoEvento();
}
