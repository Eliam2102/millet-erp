using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Events;
using Millet.Compras.Domain.Ports.OrdenCompra;
using Millet.Compras.Infrastructure;

using Millet.Compras.IntegrationTests.Fixtures;

namespace Millet.Compras.IntegrationTests.Recepcion;

/// <summary>
/// Tests integration de F5-PR1: el flujo de recepción + cierre + saldo
/// no surtido. Se ejercita publicando los eventos del puerto OC
/// directamente vía <c>IMediator</c>; no hay endpoint HTTP porque el
/// flujo es event-driven (cuando exista submódulo OC real, él emite).
///
/// <para>
/// El estado inicial requiere una RQ en <c>EnSurtido</c>: usamos el
/// flujo end-to-end (crear → transmitir → autorizar) con el stub de
/// stock al 0% para forzar bifurcación a OC.
/// </para>
/// </summary>
public class RecepcionEndpointsTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";
    private static readonly Guid ArticuloSinStock = Guid.Parse("00000000-0000-0000-0000-000000000bbb");

    private readonly StubsWebApplicationFactory _factory;

    public RecepcionEndpointsTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OcRecepcionRegistrada_RecepcionParcial_ActualizaCantidadYDejaEnSurtido()
    {
        var (rqId, lineaId) = await CrearRqEnSurtidoAsync(cantidad: 10m);

        await PublishAsync(new OcRecepcionRegistradaEvent(
            RequisicionId: rqId,
            LineaRequisicionId: lineaId,
            OrdenCompraId: Guid.CreateVersion7(),
            EmpresaId: await GetEmpresaIdAsync(),
            CantidadRecibida: 4m,
            OcurridoEn: DateTimeOffset.UtcNow));

        // Estado sigue en EnSurtido (3 = EnSurtido).
        var (estado, recibida, pendiente) = await GetLineaSnapshotAsync(rqId);
        Assert.Equal(EstadoRequisicion.EnSurtido, estado);
        Assert.Equal(4m, recibida);
        Assert.Equal(6m, pendiente); // 10 - 0 almacén - 4 recibida
    }

    [Fact]
    public async Task OcRecepcionRegistrada_RecepcionTotal_AcumulaRecibida_NoCierra()
    {
        // ADR-0043 #3: la recepción YA NO cierra la RQ; acumula CantidadRecibida
        // y la RQ se queda en EnSurtido (cierra por entrega, no por recepción).
        var (rqId, lineaId) = await CrearRqEnSurtidoAsync(cantidad: 10m);

        await PublishAsync(new OcRecepcionRegistradaEvent(
            RequisicionId: rqId,
            LineaRequisicionId: lineaId,
            OrdenCompraId: Guid.CreateVersion7(),
            EmpresaId: await GetEmpresaIdAsync(),
            CantidadRecibida: 10m,
            OcurridoEn: DateTimeOffset.UtcNow));

        var (estado, recibida, pendiente) = await GetLineaSnapshotAsync(rqId);
        Assert.Equal(EstadoRequisicion.EnSurtido, estado); // ya no cierra
        Assert.Equal(10m, recibida);
        Assert.Equal(0m, pendiente); // cubierto, pero NO cerrado
    }

    [Fact]
    public async Task OcRecepcionRegistrada_RecepcionAcumulativa_NoCierra()
    {
        var (rqId, lineaId) = await CrearRqEnSurtidoAsync(cantidad: 10m);
        var ocId = Guid.CreateVersion7();
        var empresaId = await GetEmpresaIdAsync();

        await PublishAsync(new OcRecepcionRegistradaEvent(
            rqId, lineaId, ocId, empresaId, 4m, DateTimeOffset.UtcNow));
        var midState = (await GetLineaSnapshotAsync(rqId)).Estado;
        Assert.Equal(EstadoRequisicion.EnSurtido, midState);

        await PublishAsync(new OcRecepcionRegistradaEvent(
            rqId, lineaId, ocId, empresaId, 6m, DateTimeOffset.UtcNow));
        var finalState = (await GetLineaSnapshotAsync(rqId)).Estado;
        Assert.Equal(EstadoRequisicion.EnSurtido, finalState); // recepción total ya no cierra
    }

    // --- OcCerrada → SaldoNoSurtidoEvent ---

    [Fact]
    public async Task OcCerrada_ConSaldo_PublicaSaldoNoSurtidoEvent()
    {
        // Capture handler: registra el evento de saldo cuando se publique.
        var captured = new List<SaldoNoSurtidoEvent>();
        await using var captureFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<INotificationHandler<SaldoNoSurtidoEvent>>(
                    new CapturingHandler<SaldoNoSurtidoEvent>(captured));
            });
        });

        var rqId = Guid.CreateVersion7();
        var lineaId = Guid.CreateVersion7();
        var ocId = Guid.CreateVersion7();
        var empresaId = Guid.CreateVersion7();

        using var scope = captureFactory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Publish(new OcCerradaEvent(
            RequisicionId: rqId,
            LineaRequisicionId: lineaId,
            OrdenCompraId: ocId,
            EmpresaId: empresaId,
            CantidadSolicitada: 10m,
            CantidadEntregada: 7m,
            OcurridoEn: DateTimeOffset.UtcNow));

        Assert.Single(captured);
        var saldo = captured.Single();
        Assert.Equal(rqId, saldo.RequisicionId);
        Assert.Equal(3m, saldo.Saldo);
        Assert.Equal(7m, saldo.CantidadEntregada);
    }

    [Fact]
    public async Task OcCerrada_SinSaldo_NoPublicaEvento()
    {
        var captured = new List<SaldoNoSurtidoEvent>();
        await using var captureFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<INotificationHandler<SaldoNoSurtidoEvent>>(
                    new CapturingHandler<SaldoNoSurtidoEvent>(captured));
            });
        });

        using var scope = captureFactory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Publish(new OcCerradaEvent(
            RequisicionId: Guid.CreateVersion7(),
            LineaRequisicionId: Guid.CreateVersion7(),
            OrdenCompraId: Guid.CreateVersion7(),
            EmpresaId: Guid.CreateVersion7(),
            CantidadSolicitada: 10m,
            CantidadEntregada: 10m,
            OcurridoEn: DateTimeOffset.UtcNow));

        Assert.Empty(captured);
    }

    // --- Helpers ---

    private sealed class CapturingHandler<T> : INotificationHandler<T> where T : INotification
    {
        private readonly List<T> _captured;
        public CapturingHandler(List<T> captured) { _captured = captured; }
        public Task Handle(T notification, CancellationToken cancellationToken)
        {
            _captured.Add(notification);
            return Task.CompletedTask;
        }
    }

    private async Task PublishAsync(INotification evento)
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<Millet.SharedKernel.Application.ICurrentEmpresaContext>();
        // Bypass para que el handler interno (que carga la RQ) no falle por
        // falta de empresa actual (no hay request HTTP).
        using var bypass = empresaContext.Bypass();
        await mediator.Publish(evento);
    }

    private async Task<(EstadoRequisicion Estado, decimal Recibida, decimal Pendiente)> GetLineaSnapshotAsync(Guid rqId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<Millet.SharedKernel.Application.ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        var rq = await db.Requisiciones
            .Include(r => r.Lineas)
            .AsNoTracking()
            .FirstAsync(r => r.Id == rqId);
        var linea = rq.Lineas.Single();
        return (rq.Estado, linea.CantidadRecibida, linea.Cubrimiento.CantidadPendiente);
    }

    private async Task<Guid> GetEmpresaIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<Millet.SharedKernel.Application.ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        return await db.Requisiciones.AsNoTracking().Select(r => r.EmpresaId).FirstAsync();
    }

    private async Task<(Guid RqId, Guid LineaId)> CrearRqEnSurtidoAsync(decimal cantidad)
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearTransmitirAutorizarAsync(client, ArticuloSinStock, cantidad);
        var lineaId = await GetLineaIdAsync(rqId);
        return (rqId, lineaId);
    }

    private async Task<Guid> GetLineaIdAsync(Guid rqId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<Millet.SharedKernel.Application.ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        return await db.LineaRequisiciones.AsNoTracking()
            .Where(l => l.RequisicionId == rqId)
            .Select(l => l.Id)
            .FirstAsync();
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
        var rqBody = TestComprasFixtures.BuildCrearRqValidBody(
            descripcion: "Test integration F5-PR1");
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
        var linea = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/lineas", lineaBody);
        linea.EnsureSuccessStatusCode();

        var transmit = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{rqId}/transmitir", content: null);
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
