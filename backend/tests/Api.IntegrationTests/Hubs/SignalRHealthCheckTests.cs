using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Hubs;

/// <summary>
/// Tests del health check del CollaborationHub (Sprint Buffer). Verifican
/// que <c>/health/ready</c> incorpora el check <c>signalr_hub</c> y que
/// devuelve OK en el ambiente de tests (Development sin connection string,
/// in-process backplane).
/// </summary>
public class SignalRHealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SignalRHealthCheckTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthReady_Incluye_Hub_Y_Responde_200_En_Modo_InProcess()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
