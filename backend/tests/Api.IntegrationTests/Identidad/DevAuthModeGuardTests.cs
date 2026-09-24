using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Millet.Api.IntegrationTests.Identidad;

public class DevAuthModeGuardTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DevAuthModeGuardTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FakeLogin_Se_Oculta_En_Development_Con_Auth_EntraId()
    {
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:Mode"] = "EntraId",
                    ["Entra:Provision:Disabled"] = "true"
                })));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin",
            Email = "superadmin@dev.local",
            Nombre = "Super Admin Dev",
            EmpresaId = (Guid?)null
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
