using System.Runtime.CompilerServices;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Millet.Compras.IntegrationTests;

/// <summary>
/// Inicialización del assembly de tests (F6-PR2): se ejecuta una sola
/// vez al cargar la DLL. Setea env vars que el host lee en arranque.
///
/// <para>
/// <c>Outbox__Disabled=true</c>: deshabilita el <c>BackgroundService</c>
/// del <c>OutboxPublisherWorker</c> en el host de tests para que su
/// loop no procese filas en paralelo con tests que manipulan el
/// outbox. Los tests instancian el worker manualmente para controlar
/// el tick.
/// </para>
/// </summary>
internal static class TestAssemblyInit
{
    [ModuleInitializer]
    public static void Init()
    {
        // PR B (Integraciones.Aw) — named options por DbContext. Seteamos
        // ambas para que ningún worker tickee mientras tests manipulan
        // outbox o filas relacionadas. El env var raíz queda como
        // cinturón histórico.
        Environment.SetEnvironmentVariable("Outbox__Disabled", "true");
        Environment.SetEnvironmentVariable("Compras__Outbox__Disabled", "true");
        Environment.SetEnvironmentVariable("IntegracionesAw__Outbox__Disabled", "true");

        // Identidad de los tests independiente de la configuración local
        // (user-secrets con tenant real): mismo criterio que Api.IntegrationTests.
        Environment.SetEnvironmentVariable("Auth__Mode", "FakeForLocalDev");
        Environment.SetEnvironmentVariable("Entra__Proveedor", "Simulado");
        Environment.SetEnvironmentVariable("Auth__InitialAdminEntraOid", "dev-superadmin");
    }
}
