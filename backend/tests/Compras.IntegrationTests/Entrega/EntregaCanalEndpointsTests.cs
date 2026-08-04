using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Application.Almacen;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Events;
using Millet.Compras.Domain.Ports.OrdenCompra;
using Millet.Compras.Infrastructure;
using Millet.Compras.IntegrationTests.Fixtures;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests.Entrega;

/// <summary>
/// Tests de integración del canal de entrega Almacén→Compras (ADR-0043,
/// PR #1). Ejercitan el handler <see cref="SalidaRequisicionEnAlmacenHandler"/>
/// contra Postgres real (acumular <c>CantidadEntregada</c> requiere
/// <c>Include(Lineas)</c> que materializa el VO Money — InMemory no lo shapea).
///
/// <para>Cubren las tres propiedades del PR #1: acumulación, idempotencia
/// (<see cref="EventoProcesado"/>) y CONVIVENCIA (entrega sobre RQ ya Cerrada
/// por el cierre viejo → acumula, no truena, no transiciona).</para>
/// </summary>
public class EntregaCanalEndpointsTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";
    private static readonly Guid ArticuloSinStock = Guid.Parse("00000000-0000-0000-0000-000000000bbb");

    private readonly StubsWebApplicationFactory _factory;

    public EntregaCanalEndpointsTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SalidaRegistrada_AcumulaCantidadEntregada_SinCerrar()
    {
        var (rqId, lineaId) = await CrearRqEnSurtidoAsync(cantidad: 10m);
        var empresaId = await GetEmpresaIdAsync();
        // Recepción parcial para que el techo (almacén 0 + recibida 5) admita
        // la entrega sin disparar la advertencia de exceso.
        await PublishAsync(new OcRecepcionRegistradaEvent(
            rqId, lineaId, Guid.CreateVersion7(), empresaId, 5m, DateTimeOffset.UtcNow));

        await SendEntregaAsync(Guid.NewGuid(), BuildPayload(rqId, lineaId, cantidad: 3m, empresaId));

        var (estado, entregada) = await GetEntregaSnapshotAsync(rqId);
        Assert.Equal(3m, entregada);
        Assert.Equal(EstadoRequisicion.EnSurtido, estado); // 3 < 10 → no cierra
    }

    [Fact]
    public async Task SalidaRegistrada_MismoEvento_EsIdempotente_NoDuplica()
    {
        var (rqId, lineaId) = await CrearRqEnSurtidoAsync(cantidad: 10m);
        var empresaId = await GetEmpresaIdAsync();
        await PublishAsync(new OcRecepcionRegistradaEvent(
            rqId, lineaId, Guid.CreateVersion7(), empresaId, 5m, DateTimeOffset.UtcNow));

        var eventoId = Guid.NewGuid();
        var payload = BuildPayload(rqId, lineaId, cantidad: 3m, empresaId);

        await SendEntregaAsync(eventoId, payload);
        await SendEntregaAsync(eventoId, payload); // reentrega del MISMO evento

        var (_, entregada) = await GetEntregaSnapshotAsync(rqId);
        Assert.Equal(3m, entregada); // no 6 — dedupe por EventoProcesado
    }

    [Fact]
    public async Task SalidaRegistrada_SobreRqCerradaManualmente_Acumula_NoTruena_NoTransiciona()
    {
        // ADR-0043 #3: la RQ terminal ya no viene del cierre viejo (eliminado)
        // sino del CIERRE MANUAL. Caso diseñado: material en vuelo que llega
        // tras un cierre manual. La entrega debe acumular sin transicionar
        // (tolerancia del canal a RQ terminal — conservada del cierre dormido #1).
        var client = await CreateSuperAdminClientAsync();
        var (rqId, lineaId) = await CrearRqEnSurtidoAsync(cantidad: 10m);
        var empresaId = await GetEmpresaIdAsync();

        // Cierre manual → CerradaSinSurtir (terminal). Motivo CIERRE-NO-REQ.
        var cerrar = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/cerrar-manual",
            new { MotivoId = Guid.Parse("00000003-0002-0000-0000-000000000007"), MotivoTexto = (string?)null });
        cerrar.EnsureSuccessStatusCode();
        var (estadoTrasCierre, _) = await GetEntregaSnapshotAsync(rqId);
        Assert.Equal(EstadoRequisicion.CerradaSinSurtir, estadoTrasCierre);

        await SendEntregaAsync(Guid.NewGuid(), BuildPayload(rqId, lineaId, cantidad: 4m, empresaId));

        var (estado, entregada) = await GetEntregaSnapshotAsync(rqId);
        Assert.Equal(EstadoRequisicion.CerradaSinSurtir, estado); // NO transiciona
        Assert.Equal(4m, entregada);                              // acumuló
    }

    // --- Helpers ---

    private static SalidaRequisicionRegistradaAlmacenPayload BuildPayload(
        Guid rqId, Guid lineaRqId, decimal cantidad, Guid empresaId) =>
        new(
            EmpresaId: empresaId,
            OcurridoEn: DateTimeOffset.UtcNow,
            SalidaId: Guid.CreateVersion7(),
            FolioSalida: "SAL2026-000001",
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

    private async Task<(EstadoRequisicion Estado, decimal Entregada)> GetEntregaSnapshotAsync(Guid rqId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        var rq = await db.Requisiciones.Include(r => r.Lineas).AsNoTracking()
            .FirstAsync(r => r.Id == rqId);
        return (rq.Estado, rq.Lineas.Single().CantidadEntregada);
    }

    private async Task<Guid> GetEmpresaIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        return await db.Requisiciones.AsNoTracking().Select(r => r.EmpresaId).FirstAsync();
    }

    private async Task<(Guid RqId, Guid LineaId)> CrearRqEnSurtidoAsync(decimal cantidad)
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearTransmitirAutorizarAsync(client, ArticuloSinStock, cantidad);
        Guid lineaId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();
            lineaId = await db.LineaRequisiciones.AsNoTracking()
                .Where(l => l.RequisicionId == rqId).Select(l => l.Id).FirstAsync();
        }
        return (rqId, lineaId);
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
        var rqBody = TestComprasFixtures.BuildCrearRqValidBody(descripcion: "Test integration ADR-0043 entrega");
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
            CentroCostoId = (Guid?)TestComprasFixtures.CentroCostoMaquinaSeed,
            Proyecto = (string?)null,
            FechaRequerida = (DateOnly?)null,
            Notas = (string?)null,
        };
        var linea = await client.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/lineas", lineaBody);
        linea.EnsureSuccessStatusCode();

        var transmit = await client.PostAsync($"/api/v1/compras/requisiciones/{rqId}/transmitir", content: null);
        transmit.EnsureSuccessStatusCode();

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
