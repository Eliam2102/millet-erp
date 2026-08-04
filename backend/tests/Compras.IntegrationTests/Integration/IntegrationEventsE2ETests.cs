using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.OrdenCompra;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;

using Millet.Compras.IntegrationTests.Fixtures;

namespace Millet.Compras.IntegrationTests.Integration;

/// <summary>
/// Tests E2E (F6-PR3) que verifican que cada comando que dispara un
/// flujo de negocio termina con la fila de integration event correcta
/// en <c>compras.integration_events_outbox</c>:
/// <list type="bullet">
///   <item>autorizar (stock total) → autorizada + cerrada.</item>
///   <item>autorizar (stock cero) → autorizada.</item>
///   <item>rechazar → rechazada.</item>
///   <item>eliminar → eliminada.</item>
///   <item>cancelar → cancelada.</item>
///   <item>recepción que cierra → cerrada.</item>
///   <item>OcCerrada con saldo → saldoNoSurtido.</item>
/// </list>
///
/// <para>
/// Cada test filtra outbox por <c>RequisicionId</c> de su propio flujo
/// (BD compartida con otras suites; no asume estado limpio). El payload
/// JSON deserializa al integration event concreto para validar shape.
/// </para>
/// </summary>
public class IntegrationEventsE2ETests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";
    private static readonly Guid ArticuloSeedId = Guid.Parse("00000005-0002-0000-0000-000000000001");
    private static readonly Guid ArticuloSinStock = Guid.Parse("00000000-0000-0000-0000-000000000bbb");
    private static readonly Guid MotivoSinTextoLibreId = Guid.Parse("00000003-0002-0000-0000-000000000001"); // RECH-DUP

    private readonly StubsWebApplicationFactory _factory;

    public IntegrationEventsE2ETests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // REGRESIÓN PRE-EXISTENTE: stub→adapter real (PR #293/#301).
    // Ver doc 01 §13 Rev. 21 hallazgo lateral C (patrón sistémico
    // stub-vs-adapter; la causa raíz técnica está documentada en B).
    [Fact]
    public async Task Autorizar_Persiste_AutorizadaEvent_SinCerradaEvent_TrasConmutacion()
    {
        // ADR-0043 #3: el cubrimiento (incluido 100% stock) YA NO cierra al
        // autorizar → NO se emite compras.requisicion.cerrada.v1; la RQ queda
        // EnSurtido. Solo persiste autorizada.v1.
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearTransmitirAsync(client, articuloId: ArticuloSeedId, cantidad: 10m);

        var auth = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });
        auth.EnsureSuccessStatusCode();

        var rows = await GetOutboxRowsForRqAsync(rqId);
        Assert.Contains(rows, r => r.EventType == "compras.requisicion.autorizada.v1");
        Assert.DoesNotContain(rows, r => r.EventType == "compras.requisicion.cerrada.v1");
    }

    [Fact]
    public async Task Autorizar_StockCero_Persiste_SoloAutorizadaEvent()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearTransmitirAsync(client, articuloId: ArticuloSinStock, cantidad: 10m);

        var auth = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });
        auth.EnsureSuccessStatusCode();

        var rows = await GetOutboxRowsForRqAsync(rqId);
        Assert.Contains(rows, r => r.EventType == "compras.requisicion.autorizada.v1");
        Assert.DoesNotContain(rows, r => r.EventType == "compras.requisicion.cerrada.v1");
    }

    [Fact]
    public async Task Rechazar_Persiste_RechazadaEvent_ConMotivoYActor()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearTransmitirAsync(client, articuloId: ArticuloSeedId, cantidad: 10m);

        var rechazar = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/rechazar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });
        rechazar.EnsureSuccessStatusCode();

        var rows = await GetOutboxRowsForRqAsync(rqId);
        var rechazada = rows.SingleOrDefault(r => r.EventType == "compras.requisicion.rechazada.v1");
        Assert.NotNull(rechazada);
        var payload = JsonSerializer.Deserialize<JsonElement>(rechazada!.Payload);
        Assert.Equal(rqId, payload.GetProperty("RequisicionId").GetGuid());
        Assert.Equal(MotivoSinTextoLibreId, payload.GetProperty("MotivoId").GetGuid());
    }

    [Fact]
    public async Task Eliminar_Persiste_EliminadaEvent()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearRequisicionAsync(client);

        var eliminar = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/eliminar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });
        eliminar.EnsureSuccessStatusCode();

        var rows = await GetOutboxRowsForRqAsync(rqId);
        Assert.Contains(rows, r => r.EventType == "compras.requisicion.eliminada.v1");
    }

    [Fact]
    public async Task Cancelar_Persiste_CanceladaEvent()
    {
        var client = await CreateSuperAdminClientAsync();
        // Stock cero → EnSurtido → cancelar permitido.
        var rqId = await CrearTransmitirAutorizarAsync(client, articuloId: ArticuloSinStock, cantidad: 10m);

        var cancelar = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/cancelar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });
        cancelar.EnsureSuccessStatusCode();

        var rows = await GetOutboxRowsForRqAsync(rqId);
        Assert.Contains(rows, r => r.EventType == "compras.requisicion.cancelada.v1");
    }

    [Fact]
    public async Task Recepcion_NoCierra_NoPersisteCerradaEvent_TrasConmutacion()
    {
        // ADR-0043 #3: la recepción YA NO cierra la RQ → NO emite cerrada.v1;
        // la RQ se queda EnSurtido (cierra por entrega, no por recepción).
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearTransmitirAutorizarAsync(client, articuloId: ArticuloSinStock, cantidad: 10m);
        var lineaId = await GetLineaIdAsync(rqId);
        var empresaId = await GetEmpresaIdAsync(rqId);

        // Publica el evento OC vía mediator, simulando el submódulo OC.
        await PublishViaMediatorAsync(new OcRecepcionRegistradaEvent(
            RequisicionId: rqId,
            LineaRequisicionId: lineaId,
            OrdenCompraId: Guid.CreateVersion7(),
            EmpresaId: empresaId,
            CantidadRecibida: 10m, // cubre la línea, pero ya NO cierra
            OcurridoEn: DateTimeOffset.UtcNow));

        var rows = await GetOutboxRowsForRqAsync(rqId);
        Assert.DoesNotContain(rows, r => r.EventType == "compras.requisicion.cerrada.v1");
    }

    [Fact]
    public async Task OcCerradaConSaldo_Persiste_SaldoNoSurtidoEvent()
    {
        var rqId = Guid.CreateVersion7();
        var lineaId = Guid.CreateVersion7();
        var ocId = Guid.CreateVersion7();
        var empresaId = Guid.CreateVersion7();

        // Necesitamos: OcCerradaListener → SaldoNoSurtidoEvent → mapper.
        // Para que se persista en outbox necesitamos un SaveChanges DESPUÉS
        // del Publish. Usamos un scope con DbContext y forzamos SaveChanges
        // tras el publish.
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        await mediator.Publish(new OcCerradaEvent(
            RequisicionId: rqId,
            LineaRequisicionId: lineaId,
            OrdenCompraId: ocId,
            EmpresaId: empresaId,
            CantidadSolicitada: 10m,
            CantidadEntregada: 7m,
            OcurridoEn: DateTimeOffset.UtcNow));

        // Forzar SaveChanges para que el OutboxSaveChangesInterceptor
        // drene el buffer (poblado por el mapper).
        await db.SaveChangesAsync();

        var rows = await GetOutboxRowsForEmpresaAsync(empresaId);
        var saldo = rows.SingleOrDefault(r => r.EventType == "compras.requisicion.saldoNoSurtido.v1");
        Assert.NotNull(saldo);
        var payload = JsonSerializer.Deserialize<JsonElement>(saldo!.Payload);
        Assert.Equal(rqId, payload.GetProperty("RequisicionId").GetGuid());
        Assert.Equal(3m, payload.GetProperty("Saldo").GetDecimal());
    }

    // --- Helpers ---

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<List<IntegrationEventOutboxEntry>> GetOutboxRowsForRqAsync(Guid rqId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        // Cada integration event tiene RequisicionId en el payload JSON.
        // Comparamos como string (jsonb_path_query_first o ->) — usamos
        // ->> para extraer el valor de string del campo RequisicionId.
        return await db.OutboxEntries
            .FromSql($@"
                SELECT * FROM compras.integration_events_outbox
                WHERE payload->>'RequisicionId' = {rqId.ToString()}")
            .AsNoTracking()
            .ToListAsync();
    }

    private async Task<List<IntegrationEventOutboxEntry>> GetOutboxRowsForEmpresaAsync(Guid empresaId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        return await db.OutboxEntries
            .AsNoTracking()
            .Where(e => e.IntegrationEmpresaId == empresaId)
            .ToListAsync();
    }

    private async Task<Guid> GetLineaIdAsync(Guid rqId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        return await db.LineaRequisiciones.AsNoTracking()
            .Where(l => l.RequisicionId == rqId)
            .Select(l => l.Id).FirstAsync();
    }

    private async Task<Guid> GetEmpresaIdAsync(Guid rqId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        return await db.Requisiciones.AsNoTracking()
            .Where(r => r.Id == rqId)
            .Select(r => r.EmpresaId).FirstAsync();
    }

    private async Task PublishViaMediatorAsync(INotification evento)
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        await mediator.Publish(evento);
    }

    private static async Task<Guid> CrearRequisicionAsync(HttpClient client)
    {
        var body = TestComprasFixtures.BuildCrearRqValidBody(descripcion: "F6-PR3 E2E test");
        var response = await client.PostAsJsonAsync("/api/v1/compras/requisiciones", body);
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        return json.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CrearTransmitirAsync(HttpClient client, Guid articuloId, decimal cantidad)
    {
        var rqId = await CrearRequisicionAsync(client);
        var lineaBody = new
        {
            ArticuloId = articuloId,
            Cantidad = cantidad,
            UnidadMedida = "PZA",
            PrecioEstimadoMonto = 15m,
            PrecioEstimadoMoneda = "MXN",
            CuentaContableId = (Guid?)null,
            CentroCostoId = (Guid?)null,
            Proyecto = (string?)null,
            FechaRequerida = (DateOnly?)null,
            Notas = (string?)null,
        };
        var linea = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/lineas", lineaBody);
        linea.EnsureSuccessStatusCode();

        var transmit = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{rqId}/transmitir", content: null);
        transmit.EnsureSuccessStatusCode();
        return rqId;
    }

    private static async Task<Guid> CrearTransmitirAutorizarAsync(HttpClient client, Guid articuloId, decimal cantidad)
    {
        var rqId = await CrearTransmitirAsync(client, articuloId, cantidad);
        var auth = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });
        auth.EnsureSuccessStatusCode();
        return rqId;
    }

    private static async Task<string> FakeLoginAsync(HttpClient client, string oid, string email, string nombre)
    {
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = email,
            Nombre = nombre,
            EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(json);
        return doc.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(json);
        return doc.RootElement.Clone();
    }
}
