using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace Millet.Api.IntegrationTests.Hubs;

/// <summary>
/// Smoke tests del <c>ComprasHub</c> (CollaborationHub Sprint 1, ADR-0001
/// + ADR-0012 Capa 2). Verifica el wiring end-to-end:
///
/// <list type="bullet">
///   <item>Sin JWT, la negociación contra <c>/hubs/compras</c> rechaza con 401.</item>
///   <item>Con JWT vía query param <c>access_token</c> (el path que usa el
///         frontend SignalR client al no poder mandar Authorization header
///         por WebSockets), la conexión se establece y métodos del hub
///         responden sin lanzar.</item>
/// </list>
///
/// <para>
/// <c>TestServer</c> no soporta WebSockets nativamente — fijamos
/// <see cref="HttpTransportType.LongPolling"/> para que la negociación use
/// el pipeline in-memory. La auth via query param funciona igual: el
/// <c>JwtBearerEvents.OnMessageReceived</c> de <c>AuthExtensions</c> lo
/// levanta para cualquier request a <c>/hubs/*</c>.
/// </para>
/// </summary>
public class ComprasHubSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string HubPath = "/hubs/compras";
    private const string SuperAdminOid = "dev-superadmin";

    private readonly WebApplicationFactory<Program> _factory;

    public ComprasHubSmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Conectar_Sin_Token_Falla_Con_401()
    {
        await using var connection = BuildConnection(accessToken: null);

        var act = async () => await connection.StartAsync();

        var ex = await Assert.ThrowsAnyAsync<Exception>(act);
        // El cliente SignalR envuelve la falla del negotiate en
        // HttpRequestException; el mensaje incluye el status 401.
        Assert.Contains("401", ex.ToString());
    }

    [Fact]
    public async Task Conectar_Con_JWT_Valido_Establece_Conexion_Y_Responde_Heartbeat()
    {
        var token = await FakeLoginAsync();
        await using var connection = BuildConnection(token);

        await connection.StartAsync();

        Assert.Equal(HubConnectionState.Connected, connection.State);
        Assert.False(string.IsNullOrEmpty(connection.ConnectionId));

        // Heartbeat es no-op en sprint 1 — solo verificamos que el método
        // del hub responde sin lanzar (el dispatcher SignalR encuentra el
        // método y la conexión sigue viva).
        await connection.InvokeAsync(nameof(Millet.Api.Hubs.ComprasHub.Heartbeat));

        await connection.InvokeAsync(
            nameof(Millet.Api.Hubs.ComprasHub.ViewingResource),
            "Requisicion",
            Guid.NewGuid());

        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    private HubConnection BuildConnection(string? accessToken)
    {
        var server = _factory.Server;
        var url = new Uri(server.BaseAddress, HubPath);

        return new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                // TestServer expone un HttpMessageHandler in-memory; SignalR
                // lo usa para que negotiate y long-polling vivan en proceso.
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (accessToken is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            })
            .Build();
    }

    private async Task<string> FakeLoginAsync()
    {
        using var http = _factory.CreateClient();
        var response = await http.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = SuperAdminOid,
            Email = "superadmin@dev.local",
            Nombre = "Super Admin Dev",
            EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
    }
}
