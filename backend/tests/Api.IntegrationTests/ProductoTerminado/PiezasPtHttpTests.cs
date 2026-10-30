using Npgsql;
using Millet.Integraciones.Aw.Infrastructure.PiezasPt;
using Millet.ProductoTerminado.Domain;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Administracion.Application.Abstractions;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Xunit.Abstractions;

namespace Millet.Api.IntegrationTests.ProductoTerminado;
[Collection(ApiCompartida.Nombre)]
public sealed class PiezasPtHttpTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
{
    private static readonly Guid Company = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private const string Base = "/api/v1/producto-terminado";
    private static async Task<HttpClient> Login(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = "dev-superadmin", Email = "dev-superadmin@test.local", Nombre = "QA PT", EmpresaId = Company });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }
    private static async Task SeedOrder(IServiceProvider sp, string number, Guid branch, int quantity)
    {
        var db = sp.GetRequiredService<FacturacionDbContext>();
        var order = PedidoFacturable.ImportarDesdeAw(Company, number, branch, Guid.NewGuid(), "DEMO LOCAL PT", 1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, "DEMO LOCAL PT-01", 1, "15");
        order.AgregarLinea(null, "DEMO LOCAL vidrio", "30171700", "H87", quantity, 100, 0, false); order.RecalcularTotal();
        db.PedidosFacturables.Add(order); await db.SaveChangesAsync();
    }
    private static RegistrarEscaneoPieza Input(string number, Guid branch, int piece = 1, string? eventId = null) =>
        new(Company, "Demo", eventId ?? Guid.NewGuid().ToString(), number, 1, piece, "DEMO-PT", branch, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Ingreso_atomico_evento_repetido_y_evento_distinto_por_pieza()
    {
        using var scope = factory.Services.CreateScope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var sp = scope.ServiceProvider; var number = "PT-" + Guid.NewGuid().ToString("N"); var branch = Guid.NewGuid();
        await SeedOrder(sp, number, branch, 3); var mediator = sp.GetRequiredService<IMediator>(); var input = Input(number, branch);
        var first = await mediator.Send(input); Assert.Equal(first, await mediator.Send(input));
        Assert.Equal("Duplicado", (await mediator.Send(input with { SourceEventId = Guid.NewGuid().ToString() })).Result);
        await mediator.Send(Input(number, branch, 2));
        var db = sp.GetRequiredService<ProductoTerminadoDbContext>(); var orderId = await db.Orders.Where(o => o.NumeroPedido == number).Select(o => o.Id).SingleAsync();
        var pieces = await db.Pieces.Where(p => p.PedidoPtId == orderId).ToListAsync();
        Assert.Equal(2, pieces.Count); Assert.All(pieces, p => { Assert.Equal(69, p.EstatusAw); Assert.Equal(branch, p.SucursalId); });
        var pieceIds = pieces.Select(p => p.Id).ToArray(); Assert.Equal(2, await db.Movements.CountAsync(m => pieceIds.Contains(m.PiezaId)));
        Assert.Equal(2, (await db.OutboxEntries.Where(e => e.EventType == "producto_terminado.pieza.ingresada.v1").Select(e => e.Payload).ToListAsync()).Count(p => p.Contains(number)));
    }
    [Fact]
    public async Task Pospuesto_reintento_y_descartar_exigen_motivo_etag_e_idempotencia()
    {
        using var client = await Login(factory);
        var number = "PT-" + Guid.NewGuid().ToString("N"); var branch = Guid.NewGuid(); Guid scanId; int version;
        using (var scope = factory.Services.CreateScope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(Input(number, branch));
            Assert.Equal("Pospuesto", result.Result); scanId = result.Id; version = result.Version;
            await SeedOrder(scope.ServiceProvider, number, branch, 3);
        }
        using var retry = await Post(client, scanId, "reintentar", "Pedido ingerido", version, Guid.NewGuid().ToString());
        retry.EnsureSuccessStatusCode(); var resultJson = await retry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Ingresada", resultJson.GetProperty("result").GetString());
        using (var scope = factory.Services.CreateScope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(Input(number, branch, 4));
            Assert.Equal("PT_PIEZA_EXCEDE_POSICION", result.Reason); scanId = result.Id; version = result.Version;
        }
        using var invalid = await Post(client, scanId, "descartar", " ", version, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        var key = Guid.NewGuid().ToString();
        using var discarded = await Post(client, scanId, "descartar", "Etiqueta dañada", version, key); discarded.EnsureSuccessStatusCode();
        using var replay = await Post(client, scanId, "descartar", "Etiqueta dañada", version, key); replay.EnsureSuccessStatusCode();
        Assert.Equal(await discarded.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        using var scope2 = factory.Services.CreateScope(); using var bypass2 = scope2.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var scan = await scope2.ServiceProvider.GetRequiredService<ProductoTerminadoDbContext>().Scans.SingleAsync(s => s.Id == scanId);
        Assert.Equal("Descartado", scan.Resultado); Assert.NotNull(scan.ResueltoPor); Assert.NotNull(scan.ResueltoAt);
        using var stale = await Post(client, scanId, "descartar", "Otro", version, Guid.NewGuid().ToString()); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }
    [Fact]
    public async Task Usuario_por_sucursal_ve_solo_su_alcance_y_bypass_ve_ambas()
    {
        var branch = Guid.NewGuid(); var otherBranch = Guid.NewGuid(); var permissions = new Permissions();
        await using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<ICurrentUserPermissions>(); s.AddSingleton<ICurrentUserPermissions>(permissions);
            s.RemoveAll<IUsuarioSucursalReadPort>(); s.AddSingleton<IUsuarioSucursalReadPort>(new Branches(branch));
        }));
        using var client = await Login(host);
        using var invalidPage = await client.GetAsync($"{Base}/piezas?page=0"); Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidPage.StatusCode);
        var number = "PT-" + Guid.NewGuid().ToString("N");
        using (var scope = host.Services.CreateScope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            await SeedOrder(scope.ServiceProvider, number, branch, 3);
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>(); await mediator.Send(Input(number, branch)); await mediator.Send(Input(number, otherBranch, 2));
        }
        using var scoped = await client.GetAsync($"{Base}/piezas?pedido={number}"); scoped.EnsureSuccessStatusCode();
        Assert.Equal(1, (await scoped.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetInt32());
        using var forbidden = await client.GetAsync($"{Base}/piezas?pedido={number}&sucursalId={otherBranch}"); Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        permissions.AllBranches = true;
        using var corporate = await client.GetAsync($"{Base}/piezas?pedido={number}"); corporate.EnsureSuccessStatusCode();
        Assert.Equal(2, (await corporate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task Pico_2500_con_10_por_ciento_repetidos_sin_perdidas_en_postgres()
    {
        using var scope = factory.Services.CreateScope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var sp = scope.ServiceProvider; var number = "PT-LOAD-" + Guid.NewGuid().ToString("N"); var branch = Guid.NewGuid();
        await SeedOrder(sp, number, branch, 2250); var mediator = sp.GetRequiredService<IMediator>(); var db = sp.GetRequiredService<ProductoTerminadoDbContext>();
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 2500; i++)
        {
            var piece = i % 2250 + 1; await mediator.Send(Input(number, branch, piece, number + "-" + piece)); db.ChangeTracker.Clear();
        }
        var orderId = await db.Orders.Where(o => o.NumeroPedido == number).Select(o => o.Id).SingleAsync();
        Assert.Equal(2250, await db.Pieces.CountAsync(p => p.PedidoPtId == orderId));
        Assert.Equal(2250, (await db.OutboxEntries.Select(e => e.Payload).ToListAsync()).Count(p => p.Contains(number)));
        Assert.Equal(2250, await db.Movements.CountAsync(m => db.Pieces.Any(p => p.PedidoPtId == orderId && p.Id == m.PiezaId)));
        output.WriteLine($"PostgreSQL: 2,500 escaneos, 2,250 piezas, 0 pérdidas en {timer.Elapsed.TotalSeconds:F3} s.");
    }
    [Fact]
    public async Task Demo_postgres_reader_keeps_all_rows_when_a_timestamp_spans_multiple_batches()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductoTerminadoDbContext>();
        var connectionString = db.Database.GetConnectionString()!; var station = Random.Shared.Next(100000, 999999); var prefix = "PT-READER-" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using (var schema = new NpgsqlCommand("""
            CREATE SCHEMA IF NOT EXISTS aw_origen;
            CREATE TABLE IF NOT EXISTS aw_origen.awbar_bearbeit (
                etikettnr varchar(100) NOT NULL, auftnr varchar(100) NOT NULL, pos integer, teile_nr integer,
                bearb_zeit timestamp without time zone NOT NULL, esnr_ist integer NOT NULL, fertig integer NOT NULL DEFAULT 1,
                PRIMARY KEY (etikettnr, esnr_ist, bearb_zeit));
            """, connection)) await schema.ExecuteNonQueryAsync();
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO aw_origen.awbar_bearbeit (etikettnr,auftnr,pos,teile_nr,bearb_zeit,esnr_ist,fertig)
            SELECT @prefix || n, '900101', 1, n, timestamp '2026-10-30 12:00:00', @station, 1 FROM generate_series(1,3) n;
            """, connection))
        { insert.Parameters.AddWithValue("prefix", prefix); insert.Parameters.AddWithValue("station", station); await insert.ExecuteNonQueryAsync(); }
        try
        {
            var reader = new PiezasPtReader(() => new NpgsqlConnection(connectionString), true); var cursor = new PtWatermark();
            var first = await reader.ReadAsync(cursor, 2, [new(station, "DEMO")], default); Assert.Equal(2, first.Count);
            cursor.Timestamp = first[^1].Time; cursor.Barcode = first[^1].Barcode; cursor.Station = first[^1].Station;
            var next = await reader.ReadAsync(cursor, 2, [new(station, "DEMO")], default); Assert.Single(next);
            Assert.Equal(3, first.Concat(next).Select(r => r.SourceEventId).Distinct().Count());
        }
        finally
        {
            await using var delete = new NpgsqlCommand("DELETE FROM aw_origen.awbar_bearbeit WHERE esnr_ist=@station AND etikettnr LIKE @prefix", connection);
            delete.Parameters.AddWithValue("station", station); delete.Parameters.AddWithValue("prefix", prefix + "%"); await delete.ExecuteNonQueryAsync();
        }
    }
    private static Task<HttpResponseMessage> Post(HttpClient client, Guid id, string action, string reason, int version, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/escaneos/{id}/{action}") { Content = JsonContent.Create(new { motivo = reason }) };
        request.Headers.Add("Idempotency-Key", key); request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return client.SendAsync(request);
    }
    private sealed class Permissions : ICurrentUserPermissions
    { public bool AllBranches; public ValueTask<bool> TieneAsync(string permission, CancellationToken cancellationToken = default) => ValueTask.FromResult(!permission.Contains("todas-sucursales", StringComparison.Ordinal) || AllBranches); }
    private sealed class Branches(Guid branch) : IUsuarioSucursalReadPort
    {
        public Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([branch]);
        public Task<bool> EstaAsociadoAsync(Guid userId, Guid id, CancellationToken ct) => Task.FromResult(id == branch);
    }
}
