using System.Runtime.CompilerServices;

namespace Millet.Api.IntegrationTests;

internal static class IntegrationTestEnvironment
{
    [ModuleInitializer]
    internal static void Configure()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("Auth__Mode", "FakeForLocalDev");
        // 86e3txv0n: sin SMTP local en CI; las pruebas que necesitan correo usan CorreoSalienteEnMemoria.
        Environment.SetEnvironmentVariable("Correo__Proveedor", "NoOp");
    }
}
