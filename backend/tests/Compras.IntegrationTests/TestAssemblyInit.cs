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
        // F1 (Parte F, base de desarrollo limpia): sin BD desechable no se
        // arranca — evita caer en `millet_dev` por defecto. Mismo criterio
        // que Api.IntegrationTests.
        if (string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")))
        {
            throw new InvalidOperationException(
                "Los tests de integración usan una BD desechable: corre " +
                "./tools/validate-integration-isolated.sh [--filter ...]");
        }

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

        // F2 (Parte F): idem Api.IntegrationTests — la fase de datos demo
        // del seed de Compartido no corre en tests.
        Environment.SetEnvironmentVariable("Seed__DatosDemo__Habilitado", "false");
    }
}
