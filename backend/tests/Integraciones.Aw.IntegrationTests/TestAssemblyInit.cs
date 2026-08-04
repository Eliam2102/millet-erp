using System.Runtime.CompilerServices;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Millet.Integraciones.Aw.IntegrationTests;

/// <summary>
/// Inicialización del assembly de tests. Setea env vars que el host lee
/// en arranque para que los workers de PR C no tickean durante los tests
/// (evita race con asserciones sobre estado de BD).
/// </summary>
internal static class TestAssemblyInit
{
    [ModuleInitializer]
    public static void Init()
    {
        // PR B (Integraciones.Aw) — outbox named options.
        Environment.SetEnvironmentVariable("Outbox__Disabled", "true");
        Environment.SetEnvironmentVariable("Compras__Outbox__Disabled", "true");
        Environment.SetEnvironmentVariable("IntegracionesAw__Outbox__Disabled", "true");

        // PR C — los workers Aw requieren ServiceBusClient en el contenedor.
        // En el host real, si no hay connection string, AddIntegracionesAwModule
        // no registra los workers (correcto en dev sin SB). Los wiring tests
        // necesitan que SÍ se registren para verificar el patrón
        // singleton+IHostedService. Set env var con string fake — el client
        // no conecta en construcción, sólo cuando StartProcessingAsync se
        // invoca (que estos tests no hacen).
        Environment.SetEnvironmentVariable(
            "ServiceBus__ConnectionString",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=x");
    }
}
