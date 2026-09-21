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
    }
}
