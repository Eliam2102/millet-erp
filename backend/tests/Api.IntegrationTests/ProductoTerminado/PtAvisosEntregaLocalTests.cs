using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Api.Infrastructure;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.Api.IntegrationTests.ProductoTerminado;

/// <summary>
/// M-046 (86e3zr6nn) · avisos de PT por el outbox con entrega local (C1.8), sin llamar a ningún consumidor a mano:
/// pedido retenido → CxC decide → PT libera, y la re-entrega de las mismas filas no duplica la decisión ni el cambio de estado.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class PtAvisosEntregaLocalTests(WebApplicationFactory<Program> factory)
{
    private const string EstadoCambiado = "producto_terminado.pedido.estado-cambiado.v1";
    private const string DecisionEmitida = "cuentas_por_cobrar.decision-liberacion.emitida.v1";

    [Fact]
    public async Task Pedido_retenido_recibe_la_decision_de_CxC_por_el_outbox_y_la_reentrega_no_duplica()
    {
        await using var app = HostConEntregaLocal();
        var sender = app.Services.GetRequiredService<IIntegrationEventBusSender>();
        Assert.IsType<LocalDispatchIntegrationEventBusSender>(sender);
        var datos = new PtDatos(app);
        var (sucursal, _) = await datos.SucursalesAsync();
        var cliente = Guid.NewGuid();
        var numero = await datos.PedidoAsync(sucursal, 1);
        await datos.PiezaAsync(numero, sucursal, 1);

        // Línea de crédito que cubre el pedido: la cascada de CxC decide Liberado.
        using (var scope = app.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var cxc = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
            cxc.LineasCredito.Add(LineaCredito.Crear(PtDatos.Empresa, cliente, "MXN", 100_000m, OrigenLineaCredito.Solunion, plazoDias: 45));
            await cxc.SaveChangesAsync();

            // Pedido completo y sin saldo cubierto: entra a 69 y publica Retenido al outbox de PT.
            var pt = scope.ServiceProvider.GetRequiredService<ProductoTerminadoDbContext>();
            var pedido = await pt.PedidosPt.Include(p => p.Posiciones).SingleAsync(p => p.EmpresaId == PtDatos.Empresa && p.NumeroPedido == numero);
            await scope.ServiceProvider.GetRequiredService<LiberacionPedido>().EntrarA69Async(pedido,
                new PedidoPtOrigen(pedido.PedidoFacturableId, numero, sucursal, "Facturado", null, 1, [], cliente, "MXN", 1_000m), CancellationToken.None);
            await pt.SaveChangesAsync();
            Assert.Equal(EstadoLiberacion.Retenido, pedido.Liberacion);
        }

        // Ticks reales: PT publica Retenido → CxC decide → CxC publica la decisión → PT libera.
        for (var i = 0; i < 3 && await LiberacionAsync(app, numero) != EstadoLiberacion.Liberado; i++)
        {
            await TickAsync<ProductoTerminadoDbContext>(app);
            await TickAsync<CuentasPorCobrarDbContext>(app);
        }
        Assert.Equal(EstadoLiberacion.Liberado, await LiberacionAsync(app, numero));
        Assert.Equal(1, await DecisionesAsync(app, numero));
        Assert.Equal(1, await FilasAsync<ProductoTerminadoDbContext>(app, numero, EstadoCambiado, "\"Liberado\""));

        // (El payload trae la propiedad LiberadoPor: se busca el valor entre comillas.)
        // Misma fila entregada otra vez (Retenido a CxC y decisión a PT): sin decisión ni liberación nuevas.
        await ReentregarAsync<ProductoTerminadoDbContext>(app, sender, numero, "Retenido");
        await ReentregarAsync<CuentasPorCobrarDbContext>(app, sender, numero, "Liberado");
        Assert.Equal(1, await DecisionesAsync(app, numero));
        Assert.Equal(1, await FilasAsync<CuentasPorCobrarDbContext>(app, numero, DecisionEmitida, numero));
        Assert.Equal(1, await FilasAsync<ProductoTerminadoDbContext>(app, numero, EstadoCambiado, "\"Liberado\""));
    }

    private WebApplicationFactory<Program> HostConEntregaLocal() =>
        factory.WithWebHostBuilder(b =>
        {
            b.UseSetting(EntregaLocalIntegrationEventsExtensions.ClaveHabilitada, "true");
            b.ConfigureTestServices(s =>
            {
                // Sin outbox de fondo (los ticks son manuales) ni listeners de Service Bus.
                var fondo = s.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType is { } t
                    && ((t.IsGenericType && t.GetGenericTypeDefinition() == typeof(OutboxPublisherWorker<>))
                        || t.Name.EndsWith("EventListenerWorker", StringComparison.Ordinal))).ToList();
                foreach (var d in fondo) s.Remove(d);
            });
        });

    private static Task TickAsync<TCtx>(WebApplicationFactory<Program> app) where TCtx : DbContext =>
        new OutboxPublisherWorker<TCtx>(app.Services.GetRequiredService<IServiceScopeFactory>(),
            app.Services.GetRequiredService<IOptionsMonitor<OutboxPublisherOptions>>(),
            NullLogger<OutboxPublisherWorker<TCtx>>.Instance).PollOnceAsync(CancellationToken.None);

    private static async Task<EstadoLiberacion?> LiberacionAsync(WebApplicationFactory<Program> app, string numero)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return (await scope.ServiceProvider.GetRequiredService<ProductoTerminadoDbContext>().PedidosPt.AsNoTracking()
            .SingleAsync(p => p.EmpresaId == PtDatos.Empresa && p.NumeroPedido == numero)).Liberacion;
    }

    private static async Task<int> DecisionesAsync(WebApplicationFactory<Program> app, string numero)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>().DecisionesLiberacion
            .CountAsync(d => d.EmpresaId == PtDatos.Empresa && d.PedidoRef == numero);
    }

    private static async Task<List<IntegrationEventOutboxEntry>> FilasDeAsync<TCtx>(WebApplicationFactory<Program> app, string numero, string tipo) where TCtx : DbContext
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var filas = await scope.ServiceProvider.GetRequiredService<TCtx>().Set<IntegrationEventOutboxEntry>().AsNoTracking()
            .Where(e => e.EventType == tipo).ToListAsync();
        return filas.Where(e => e.Payload.Contains(numero, StringComparison.Ordinal)).ToList();
    }

    private static async Task<int> FilasAsync<TCtx>(WebApplicationFactory<Program> app, string numero, string tipo, string contiene) where TCtx : DbContext =>
        (await FilasDeAsync<TCtx>(app, numero, tipo)).Count(e => e.Payload.Contains(contiene, StringComparison.Ordinal));

    private static async Task ReentregarAsync<TCtx>(WebApplicationFactory<Program> app, IIntegrationEventBusSender sender, string numero, string contiene) where TCtx : DbContext
    {
        var topic = app.Services.GetRequiredService<IOptionsMonitor<OutboxPublisherOptions>>().Get(typeof(TCtx).Name).ServiceBusTopicName;
        var tipo = typeof(TCtx) == typeof(ProductoTerminadoDbContext) ? EstadoCambiado : DecisionEmitida;
        foreach (var fila in (await FilasDeAsync<TCtx>(app, numero, tipo)).Where(e => e.Payload.Contains(contiene, StringComparison.Ordinal)))
            await sender.SendAsync(fila, topic, CancellationToken.None);
    }
}
