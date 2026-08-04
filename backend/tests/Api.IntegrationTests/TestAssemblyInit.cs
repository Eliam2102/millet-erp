using System.Runtime.CompilerServices;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Millet.Api.IntegrationTests;

/// <summary>
/// Inicialización del assembly de tests (F6-PR2): se ejecuta una sola
/// vez al cargar la DLL. Setea env vars que el host lee en arranque.
///
/// <para>
/// <c>Outbox__Disabled=true</c>: deshabilita el worker de outbox para
/// que su loop no compita con suites de Compras.IntegrationTests
/// (procesos de test paralelos comparten la misma BD dev local).
/// </para>
/// </summary>
internal static class TestAssemblyInit
{
    [ModuleInitializer]
    public static void Init()
    {
        // PR B (Integraciones.Aw) — las options del outbox ahora son named
        // por DbContext (Compras:Outbox, IntegracionesAw:Outbox). El env
        // var raíz "Outbox__Disabled" queda como cinturón histórico
        // (otros tests pueden bindar a "Outbox" plano); además seteamos
        // las secciones específicas para cada worker.
        Environment.SetEnvironmentVariable("Outbox__Disabled", "true");
        Environment.SetEnvironmentVariable("Compras__Outbox__Disabled", "true");
        Environment.SetEnvironmentVariable("IntegracionesAw__Outbox__Disabled", "true");

        // Soft locks: TTLs cortos para que los tests de expiración no
        // tengan que dormir 90s (default productivo). Los smoke tests son
        // inocuos a esta config — sus entries se releasean al disconnect.
        Environment.SetEnvironmentVariable("SoftLock__HeartbeatExpirationSeconds", "2");
        Environment.SetEnvironmentVariable("SoftLock__SweepIntervalSeconds", "1");

        // Stub de stock: forzar DefaultRatio=1.0 para que los tests de
        // autorización (que esperan que el stub cubra toda la cantidad
        // → RQ Cerrada sin OC) sigan pasando. dev local usa 0.0 en
        // appsettings.Development.json para que el flujo RQ → OC sea
        // visible al desarrollar; los tests dependen del comportamiento
        // legado y se aíslan con este override.
        Environment.SetEnvironmentVariable("Compras__Stubs__Stock__DefaultRatio", "1.0");
    }
}
