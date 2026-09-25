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
        // F1 (Parte F, base de desarrollo limpia): los tests de integración
        // usan una BD desechable, nunca `millet_dev`. Sin esta variable el
        // host caería en el `ConnectionStrings:Postgres` de
        // appsettings.Development.json (la BD de desarrollo compartida) y
        // la contaminaría con datos de prueba. Falla rápido y en español
        // en vez de dejar que el host arranque contra la BD equivocada.
        if (string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")))
        {
            throw new InvalidOperationException(
                "Los tests de integración usan una BD desechable: corre " +
                "./tools/validate-integration-isolated.sh [--filter ...]");
        }

        // PR B (Integraciones.Aw) — las options del outbox ahora son named
        // por DbContext (Compras:Outbox, IntegracionesAw:Outbox). El env
        // var raíz "Outbox__Disabled" queda como cinturón histórico
        // (otros tests pueden bindar a "Outbox" plano); además seteamos
        // las secciones específicas para cada worker.
        Environment.SetEnvironmentVariable("Outbox__Disabled", "true");
        Environment.SetEnvironmentVariable("Compras__Outbox__Disabled", "true");
        Environment.SetEnvironmentVariable("IntegracionesAw__Outbox__Disabled", "true");

        // Provisión de cuentas Entra (plan 15, F4): el worker procesa
        // TODOS los usuarios en ProvisionandoCuenta de la BD compartida;
        // con su ciclo encendido en cualquier host de tests se llevaría
        // los usuarios de ColaboradoresCuentaNuevaTests a otro directorio
        // simulado. Esas pruebas corren el ciclo a mano.
        Environment.SetEnvironmentVariable("Entra__Provision__Disabled", "true");

        // Identidad de los tests independiente de la configuración local del
        // desarrollador: quien prueba contra un tenant real guarda Graph y su
        // OID en user-secrets (que el host de tests también carga en
        // Development). Las variables de entorno pesan más: los tests siempre
        // usan el directorio simulado y el SuperAdmin sintético 'dev-superadmin'.
        Environment.SetEnvironmentVariable("Auth__Mode", "FakeForLocalDev");
        Environment.SetEnvironmentVariable("Entra__Proveedor", "Simulado");
        Environment.SetEnvironmentVariable("Auth__InitialAdminEntraOid", "dev-superadmin");

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

        // F2 (Parte F): la fase de datos demo del seed de Compartido solo
        // corre si `Seed:DatosDemo:Habilitado=true` (dev local). Los tests
        // fuerzan `false` — dependen únicamente del seed de prueba
        // existente (TestSucursales/TestDepartamentos/etc.), no de los
        // datos demo, que no son deterministas frente a los asserts de
        // conteo de la suite.
        Environment.SetEnvironmentVariable("Seed__DatosDemo__Habilitado", "false");
    }
}
