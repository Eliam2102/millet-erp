using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Millet.Api.Hubs;

namespace Millet.Api.IntegrationTests.Hubs;

/// <summary>
/// Behavior tests del soft-lock manager (CollaborationHub Sprint 2,
/// ADR-0012 Capa 2). Las pruebas conectan dos <see cref="HubConnection"/>
/// como el mismo usuario superadmin (mismo grupo de empresa) y verifican
/// que las acciones de la conexión A llegan a la conexión B vía el
/// broadcast <c>userPresence</c>.
///
/// <para>
/// Compartir <c>userId</c> entre las dos conexiones es intencional: el
/// fixture de fake-login auto-provisiona un usuario con 0 empresas si el
/// oid es nuevo, y no hay path en dev para asignarle empresa al vuelo.
/// El manager filtra por <c>(empresaId, entidad, entidadId, connectionId)</c>,
/// así que dos conexiones con el mismo userId pero distintos connectionIds
/// son entries independientes y prueban correctamente el flujo de
/// presencia.
/// </para>
///
/// <para>
/// Los TTLs cortos (HeartbeatExpiration=2s, Sweep=1s) los configura
/// <see cref="TestAssemblyInit"/> para evitar dormir 90s en tests.
/// </para>
/// </summary>
public class SoftLockBehaviorTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string HubPath = "/hubs/compras";
    private const string SuperAdminOid = "dev-superadmin";
    private static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ExpirationTimeout = TimeSpan.FromSeconds(8);

    private readonly WebApplicationFactory<Program> _factory;

    public SoftLockBehaviorTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ViewingResource_Notifica_A_Otras_Conexiones_Del_Mismo_Grupo()
    {
        var token = await FakeLoginAsync();
        await using var connA = await ConnectAsync(token);
        await using var connB = await ConnectAsync(token);

        var listener = new PresenceListener(connB);
        var requisicionId = Guid.NewGuid();

        await connA.InvokeAsync(nameof(ComprasHub.ViewingResource), "Requisicion", requisicionId);

        var push = await listener.WaitForAsync(PushTimeout);
        Assert.Equal("Requisicion", push.Entidad);
        Assert.Equal(requisicionId, push.EntidadId);
        Assert.Single(push.Users);
        Assert.Equal("Viewing", push.Users[0].Modo);
    }

    [Fact]
    public async Task LeaveResource_Notifica_Lista_Vacia()
    {
        var token = await FakeLoginAsync();
        await using var connA = await ConnectAsync(token);
        await using var connB = await ConnectAsync(token);

        var listener = new PresenceListener(connB);
        var requisicionId = Guid.NewGuid();

        await connA.InvokeAsync(nameof(ComprasHub.EditingResource), "Requisicion", requisicionId);
        var first = await listener.WaitForAsync(PushTimeout);
        Assert.Single(first.Users);

        listener.Reset();
        await connA.InvokeAsync(nameof(ComprasHub.LeaveResource));

        var second = await listener.WaitForAsync(PushTimeout);
        Assert.Empty(second.Users);
    }

    [Fact]
    public async Task Disconnect_Notifica_Lista_Vacia()
    {
        var token = await FakeLoginAsync();
        var connA = await ConnectAsync(token);
        await using var connB = await ConnectAsync(token);

        var listener = new PresenceListener(connB);
        var requisicionId = Guid.NewGuid();

        await connA.InvokeAsync(nameof(ComprasHub.ViewingResource), "Requisicion", requisicionId);
        await listener.WaitForAsync(PushTimeout);

        listener.Reset();
        await connA.DisposeAsync();

        var afterDisconnect = await listener.WaitForAsync(PushTimeout);
        Assert.Empty(afterDisconnect.Users);
    }

    [Fact]
    public async Task GetPresence_Retorna_Snapshot_Inicial_Sin_Esperar_Push()
    {
        var token = await FakeLoginAsync();
        await using var connA = await ConnectAsync(token);
        await using var connB = await ConnectAsync(token);

        var requisicionId = Guid.NewGuid();
        await connA.InvokeAsync(nameof(ComprasHub.EditingResource), "Requisicion", requisicionId);

        // Espera 200ms para que el broadcast del Track llegue (defensivo —
        // no bloqueante, GetPresence lee del manager directamente).
        await Task.Delay(200);

        var snapshot = await connB.InvokeAsync<List<UserPresenceDto>>(
            nameof(ComprasHub.GetPresence), "Requisicion", requisicionId);

        Assert.Single(snapshot);
        Assert.Equal(SoftLockModo.Editing, snapshot[0].Modo);
    }

    [Fact]
    public async Task Sin_Heartbeat_El_Worker_Expira_La_Entry()
    {
        var token = await FakeLoginAsync();
        await using var connA = await ConnectAsync(token);
        await using var connB = await ConnectAsync(token);

        var listener = new PresenceListener(connB);
        var requisicionId = Guid.NewGuid();

        await connA.InvokeAsync(nameof(ComprasHub.ViewingResource), "Requisicion", requisicionId);
        await listener.WaitForAsync(PushTimeout);

        listener.Reset();
        // Sin Heartbeat — TTL=2s configurado en TestAssemblyInit; el sweep
        // (cada 1s) lo barre pasados 2s. Esperamos hasta 8s margen.
        var expirationPush = await listener.WaitForAsync(ExpirationTimeout);
        Assert.Empty(expirationPush.Users);
    }

    private async Task<HubConnection> ConnectAsync(string accessToken)
    {
        var server = _factory.Server;
        var url = new Uri(server.BaseAddress, HubPath);

        var connection = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .Build();

        await connection.StartAsync();
        return connection;
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

    /// <summary>
    /// Suscribe a <c>userPresence</c> y permite esperar el siguiente push
    /// con timeout. Diseñado para usarse en sucesivos pasos del test:
    /// llamar <see cref="Reset"/> entre acciones para que el siguiente
    /// <see cref="WaitForAsync"/> capture solo lo nuevo.
    /// </summary>
    private sealed class PresenceListener
    {
        private TaskCompletionSource<PresencePush> _tcs;

        public PresenceListener(HubConnection connection)
        {
            _tcs = NewTcs();
            connection.On<JsonElement>("userPresence", json =>
            {
                var push = ParsePush(json);
                _tcs.TrySetResult(push);
            });
        }

        public Task<PresencePush> WaitForAsync(TimeSpan timeout)
        {
            return _tcs.Task.WaitAsync(timeout);
        }

        public void Reset() => _tcs = NewTcs();

        private static TaskCompletionSource<PresencePush> NewTcs()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static PresencePush ParsePush(JsonElement json)
        {
            var entidad = json.GetProperty("entidad").GetString()!;
            var entidadId = json.GetProperty("entidadId").GetGuid();
            var users = json.GetProperty("users").EnumerateArray()
                .Select(u => new PresenceUser(
                    UserId: u.GetProperty("userId").GetGuid(),
                    UserNombre: u.GetProperty("userNombre").GetString() ?? string.Empty,
                    Modo: u.GetProperty("modo").GetString() ?? string.Empty))
                .ToList();
            return new PresencePush(entidad, entidadId, users);
        }
    }

    private sealed record PresencePush(string Entidad, Guid EntidadId, IReadOnlyList<PresenceUser> Users);
    private sealed record PresenceUser(Guid UserId, string UserNombre, string Modo);
}
