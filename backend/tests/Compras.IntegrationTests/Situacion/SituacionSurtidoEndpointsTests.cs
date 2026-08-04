using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Application.Almacen;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.OrdenCompra;
using Millet.Compras.Infrastructure;
using Millet.Compras.IntegrationTests.Fixtures;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests.Situacion;

/// <summary>
/// Tests de integración de la situación de surtido (ADR-0043, PR1) contra
/// Postgres real. Verifican:
/// <list type="bullet">
///   <item>el list-query deriva la situación correcta por RQ (una de cada) —
///     y de paso PRUEBA que el <c>SUM(CASE WHEN ...)</c> por línea traduce a
///     SQL: si no tradujera, EF lanzaría al ejecutar el query;</item>
///   <item><b>no-drift</b>: la misma RQ por el detalle y por la lista da la
///     MISMA situación (ambos usan <see cref="SituacionSurtidoDerivacion"/>);</item>
///   <item>fuera de <c>EnSurtido</c> (Borrador) la situación es null.</item>
/// </list>
/// </summary>
public class SituacionSurtidoEndpointsTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";

    // Artículo sembrado (ART-TEST-BBB). En este setup la autorización manda TODO a
    // compra (el stub no reserva stock al autorizar), así que el artículo concreto
    // es indistinto: la disponibilidad para entregar se genera por RECEPCIÓN
    // (cant_recibida), no por cubrimiento de stock.
    private static readonly Guid Articulo = Guid.Parse("00000000-0000-0000-0000-000000000bbb");

    private readonly StubsWebApplicationFactory _factory;

    public SituacionSurtidoEndpointsTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListYDetalle_DerivanMismaSituacion_YNullFueraDeEnSurtido()
    {
        var client = await CreateSuperAdminClientAsync();
        var empresaId = await GetEmpresaIdAsync();

        // EsperandoCompra: autorizada, todo en compra, NADA recibido ni entregado.
        var rqEsperando = await CrearTransmitirAutorizarAsync(client, Articulo, 10m);

        // ListoParaSurtir: recibido (disponible para entregar) pero NADA entregado.
        var rqListo = await CrearTransmitirAutorizarAsync(client, Articulo, 10m);
        await PublishAsync(new OcRecepcionRegistradaEvent(
            rqListo, await GetPrimeraLineaIdAsync(rqListo), Guid.CreateVersion7(), empresaId, 10m, DateTimeOffset.UtcNow));

        // SurtidoParcial: recibido + entrega PARCIAL por el canal de salidas.
        var rqParcial = await CrearTransmitirAutorizarAsync(client, Articulo, 10m);
        var lineaParcial = await GetPrimeraLineaIdAsync(rqParcial);
        await PublishAsync(new OcRecepcionRegistradaEvent(
            rqParcial, lineaParcial, Guid.CreateVersion7(), empresaId, 10m, DateTimeOffset.UtcNow));
        await SendEntregaAsync(Guid.NewGuid(), BuildPayload(rqParcial, lineaParcial, cantidad: 4m, empresaId));

        // Fuera de EnSurtido: creada con línea, SIN transmitir → Borrador.
        var rqBorrador = await CrearConLineaSinTransmitirAsync(client, Articulo, 5m);

        // ── Detalle (by-id, sin paginación) ──
        Assert.Equal((int)SituacionSurtido.EsperandoCompra, await GetSituacionDetalleAsync(client, rqEsperando));
        Assert.Equal((int)SituacionSurtido.ListoParaSurtir, await GetSituacionDetalleAsync(client, rqListo));
        Assert.Equal((int)SituacionSurtido.SurtidoParcial, await GetSituacionDetalleAsync(client, rqParcial));
        Assert.Null(await GetSituacionDetalleAsync(client, rqBorrador));

        // ── Lista (ejecuta el SUM en SQL) + no-drift vs detalle ──
        var lista = await GetSituacionesDeListaAsync(client);
        Assert.Equal((int)SituacionSurtido.EsperandoCompra, lista[rqEsperando]);
        Assert.Equal((int)SituacionSurtido.ListoParaSurtir, lista[rqListo]);
        Assert.Equal((int)SituacionSurtido.SurtidoParcial, lista[rqParcial]);
        Assert.Null(lista[rqBorrador]);
    }

    /// <summary>
    /// ADR-0047 PR5.F: una RQ creada por el flujo normal (HTTP POST) tiene
    /// <c>Origen == Manual</c>, y el campo se expone tanto en el detalle
    /// (<c>RequisicionResponse</c>) como en la bandeja
    /// (<c>RequisicionListItemResponse</c>). Serializa como int (0 = Manual),
    /// sin <c>JsonStringEnumConverter</c> global — base del badge "Sistema" del FE.
    /// </summary>
    [Fact]
    public async Task BandejaYDetalle_ExponenOrigen_ManualPorDefecto()
    {
        var client = await CreateSuperAdminClientAsync();
        var rq = await CrearConLineaSinTransmitirAsync(client, Articulo, 5m);

        // Detalle (by-id)
        var detalle = await client.GetAsync($"/api/v1/compras/requisiciones/{rq}");
        detalle.EnsureSuccessStatusCode();
        var detalleRoot = await ReadJsonAsync(detalle);
        Assert.Equal((int)OrigenRequisicion.Manual, detalleRoot.GetProperty("origen").GetInt32());

        // Bandeja (list)
        var lista = await client.GetAsync("/api/v1/compras/requisiciones?limit=200");
        lista.EnsureSuccessStatusCode();
        var listaRoot = await ReadJsonAsync(lista);
        var item = listaRoot.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == rq);
        Assert.Equal((int)OrigenRequisicion.Manual, item.GetProperty("origen").GetInt32());
    }

    // ── Helpers de aserción ──

    /// <summary>Devuelve la situación (int) del detalle, o null si ausente/null.</summary>
    private static async Task<int?> GetSituacionDetalleAsync(HttpClient client, Guid rqId)
    {
        var resp = await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}");
        resp.EnsureSuccessStatusCode();
        var root = await ReadJsonAsync(resp);
        return LeerSituacion(root);
    }

    /// <summary>
    /// Mapa id→situación de la bandeja general (limit alto, orden desc por fecha
    /// → las RQs recién creadas quedan en la primera página).
    /// </summary>
    private static async Task<IReadOnlyDictionary<Guid, int?>> GetSituacionesDeListaAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/api/v1/compras/requisiciones?limit=200");
        resp.EnsureSuccessStatusCode();
        var root = await ReadJsonAsync(resp);
        var mapa = new Dictionary<Guid, int?>();
        foreach (var item in root.GetProperty("items").EnumerateArray())
        {
            mapa[item.GetProperty("id").GetGuid()] = LeerSituacion(item);
        }

        return mapa;
    }

    private static int? LeerSituacion(JsonElement obj)
    {
        if (!obj.TryGetProperty("situacionSurtido", out var v) || v.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return v.GetInt32();
    }

    // ── Helpers de seeding (patrón de EntregaCanalEndpointsTests) ──

    private async Task<Guid> GetPrimeraLineaIdAsync(Guid rqId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        return await db.LineaRequisiciones.AsNoTracking()
            .Where(l => l.RequisicionId == rqId).Select(l => l.Id).FirstAsync();
    }

    private async Task<Guid> GetEmpresaIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        return await db.Requisiciones.AsNoTracking().Select(r => r.EmpresaId).FirstAsync();
    }

    private static SalidaRequisicionRegistradaAlmacenPayload BuildPayload(
        Guid rqId, Guid lineaRqId, decimal cantidad, Guid empresaId) =>
        new(
            EmpresaId: empresaId,
            OcurridoEn: DateTimeOffset.UtcNow,
            SalidaId: Guid.CreateVersion7(),
            FolioSalida: "SAL2026-SIT-01",
            RqId: rqId,
            EsPorVale: false,
            Lineas: new[]
            {
                new LineaSalidaAlmacenPayload(
                    LineaSalidaId: Guid.CreateVersion7(),
                    ArticuloId: Guid.CreateVersion7(),
                    Cantidad: cantidad,
                    LineaRqId: lineaRqId),
            });

    private async Task SendEntregaAsync(Guid eventoId, SalidaRequisicionRegistradaAlmacenPayload payload)
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        await mediator.Send(new SalidaRequisicionEnAlmacenCommand(eventoId, payload));
    }

    private async Task PublishAsync(INotification evento)
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        await mediator.Publish(evento);
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CrearTransmitirAutorizarAsync(HttpClient client, Guid articuloId, decimal cantidad)
    {
        var rqId = await CrearConLineaSinTransmitirAsync(client, articuloId, cantidad);

        var transmit = await client.PostAsync($"/api/v1/compras/requisiciones/{rqId}/transmitir", content: null);
        transmit.EnsureSuccessStatusCode();

        var auth = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });
        auth.EnsureSuccessStatusCode();

        return rqId;
    }

    private static async Task<Guid> CrearConLineaSinTransmitirAsync(HttpClient client, Guid articuloId, decimal cantidad)
    {
        var rqBody = TestComprasFixtures.BuildCrearRqValidBody(descripcion: "Test integration ADR-0043 situación");
        var crear = await client.PostAsJsonAsync("/api/v1/compras/requisiciones", rqBody);
        crear.EnsureSuccessStatusCode();
        var rqId = (await ReadJsonAsync(crear)).GetProperty("id").GetGuid();

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
        var linea = await client.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/lineas", lineaBody);
        linea.EnsureSuccessStatusCode();

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
