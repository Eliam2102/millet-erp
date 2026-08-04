using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace Millet.Api.IntegrationTests.Hubs;

/// <summary>
/// Smoke tests del <c>IntegracionesAwHub</c> (PR C). Mismo patrón que
/// <see cref="ComprasHubSmokeTests"/> — usa TestServer in-memory +
/// LongPolling porque WebSockets no funciona con TestServer.
///
/// <list type="bullet">
///   <item>Sin JWT → 401 en negotiate.</item>
///   <item>Con SuperAdmin JWT (tiene permiso integraciones.aw.cotizaciones.consultar
///         vía rol super-admin + es asignado a la empresa bootstrap automáticamente
///         por fake-login) → conexión establecida.</item>
/// </list>
///
/// <para>
/// El test "sin current_empresa_id → Abort" requiere un usuario sin
/// asignaciones de empresa. fake-login con EmpresaId=null cae al
/// auto-pick de la primera empresa del usuario, así que el SuperAdmin
/// (asignado a la empresa bootstrap) siempre obtiene empresa. Ese caso
/// queda como tarea para test futuro con un user sin asignaciones.
/// </para>
/// </summary>
public class IntegracionesAwHubSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string HubPath = "/hubs/integraciones-aw";
    private const string SuperAdminOid = "dev-superadmin";

    private readonly WebApplicationFactory<Program> _factory;

    public IntegracionesAwHubSmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Conectar_Sin_Token_Falla_Con_401()
    {
        await using var connection = BuildConnection(accessToken: null);

        var act = async () => await connection.StartAsync();

        var ex = await Assert.ThrowsAnyAsync<Exception>(act);
        Assert.Contains("401", ex.ToString());
    }

    [Fact]
    public async Task Conectar_Con_SuperAdmin_JWT_Establece_Conexion()
    {
        var token = await FakeLoginAsync();
        await using var connection = BuildConnection(token);

        await connection.StartAsync();

        Assert.Equal(HubConnectionState.Connected, connection.State);
        Assert.False(string.IsNullOrEmpty(connection.ConnectionId));
    }

    private HubConnection BuildConnection(string? accessToken)
    {
        var server = _factory.Server;
        var url = new Uri(server.BaseAddress, HubPath);

        return new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
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
