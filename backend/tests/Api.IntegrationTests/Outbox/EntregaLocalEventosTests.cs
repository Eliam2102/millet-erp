using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.Infrastructure;
using Millet.Api.IntegrationTests.Compras;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.Stubs;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure.Outbox;
using static Millet.Api.IntegrationTests.Compras.CompraAPagoDemoTests;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Outbox;

/// <summary>
/// C1.8 — entrega local de eventos sin Service Bus (demo y desarrollo).
/// Con <c>ServiceBus:EntregaLocal:Habilitada=true</c> y sin conexión, el
/// <c>OutboxPublisherWorker</c> entrega cada fila del outbox a los listeners
/// reales de los módulos suscritos, en el mismo proceso.
///
/// <para>
/// Los <c>OutboxPublisherWorker</c> de fondo se quitan del host de prueba
/// para que no compitan con los ticks manuales (<c>PollOnceAsync</c>).
/// </para>
/// </summary>
public sealed class EntregaLocalEventosTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string FacturaRegistrada = "cuentas_por_pagar.factura.registrada.v1";

    // ---------- C1.8-a ----------
    [Fact]
    public async Task C18a_factura_de_proveedor_llega_a_Compras_por_el_outbox_de_CxP_con_entrega_local()
    {
        var desde = DateTimeOffset.UtcNow.AddSeconds(-5);
        var app = HostConEntregaLocal(b => b.ConfigureTestServices(s =>
            s.PostConfigure<ComprasStubsOptions>(o => o.Stubs.Stock.DefaultRatio = 0m)));
        var sender = app.Services.GetRequiredService<IIntegrationEventBusSender>();
        Assert.IsType<LocalDispatchIntegrationEventBusSender>(sender);

        var hoy = DateOnly.FromDateTime(DateTime.Now);
        Guid articulo;
        using (var scope = app.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var cat = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var pza = await cat.UnidadesMedida.SingleAsync(u => u.Codigo == "PZA");
            var art = new Millet.DatosMaestros.Domain.Articulo(Guid.NewGuid(), $"EL{Guid.NewGuid():N}"[..16], "C1.8 entrega local", "PZA", unidadMedidaId: pza.Id);
            cat.Add(art); await cat.SaveChangesAsync(); articulo = art.Id;
        }
        await using var periodo = await AbrirPeriodoSiHaceFaltaAsync(app, hoy);

        var a = await ClienteConPermisosAsync(app, "compras.requisiciones.crear", "compras.requisiciones.leer", "compras.requisiciones.editar", "centros_costo.dim3.leer-todos",
            "compras.ordenes.crear", "compras.ordenes.leer", "compras.ordenes.adjuntar");
        var b = await ClienteConPermisosAsync(app, PermisosCanonicos.Todos.Select(p => p.Codigo).ToArray());
        await AsociarSucursalAsync(app, a);
        await Ok(await b.PutAsJsonAsync("/api/v1/compras/umbrales-aprobacion", new { DepartamentoId = TestComprasFixtures.DeptoCompras, UmbralMenorCuantia = 1000m, UmbralMonto = 100000m, Moneda = "MXN", ModoNivel2 = "SoloMonto" }), "umbral depto");

        // RQ -> OC autorizada
        var rqResp = await a.PostAsJsonAsync("/api/v1/compras/requisiciones", TestComprasFixtures.BuildCrearRqValidBody(descripcion: "C1.8 entrega local"));
        await Ok(rqResp, "A crea RQ");
        var rqId = (await J(rqResp)).GetProperty("id").GetGuid();
        await Ok(await a.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/lineas", new
        {
            ArticuloId = articulo, Cantidad = 10m, UnidadMedida = "PZA", PrecioEstimadoMonto = 15m, PrecioEstimadoMoneda = "MXN",
            CentroCostoId = CentroCosto,
        }), "A agrega linea");
        await Ok(await a.PostAsync($"/api/v1/compras/requisiciones/{rqId}/transmitir", null), "A transmite RQ");
        await Ok(await b.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/autorizaciones", new { Nivel = 1, Notas = "C1.8" }), "B autoriza RQ");
        var ocResp = await a.PostAsJsonAsync("/api/v1/compras/ordenes/desde-requisicion", new
        {
            RequisicionId = rqId, SucursalCodigo = "MID", FolioAnio = (short)hoy.Year, ProveedorId = Proveedor,
            CondicionesPagoId = Guid.NewGuid(), UsoPrincipalId = Guid.NewGuid(), FechaDocumento = hoy,
        });
        await Ok(ocResp, "A crea OC desde RQ");
        var ocId = JsonDocument.Parse(await ocResp.Content.ReadAsStringAsync()).RootElement.GetProperty("ordenCompraId").GetGuid();
        await AdjuntarCotizacionAsync(app, ocId);
        await Ok(await a.PostAsync($"/api/v1/compras/ordenes/{ocId}/transmitir", null), "A transmite OC");
        await Ok(await b.PostAsJsonAsync($"/api/v1/compras/ordenes/{ocId}/autorizaciones", new { Nivel = 1 }), "B autoriza OC");
        var oc = await J(await b.GetAsync($"/api/v1/compras/ordenes/{ocId}"));
        Assert.Equal((int)EstadoOrdenCompra.Autorizada, oc.GetProperty("estado").GetInt32());
        var lineaOcId = oc.GetProperty("lineas")[0].GetProperty("id").GetGuid();
        var subtotal = oc.GetProperty("lineas")[0].GetProperty("subtotalLinea").GetDecimal();

        // Recepción en Almacén; OcAutorizada y OcRecepcionRegistrada viajan con el sender local.
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        var bin = await SembrarBinAsync(app, articulo);
        await Ok(await b.PostAsJsonAsync("/api/v1/almacen/recepciones/", new
        {
            OrdenCompraId = ocId, FechaMovimiento = hoy, CfdiUuidFiscal = uuid, Observaciones = "C1.8 recepcion",
            Lineas = new[] { new { ArticuloId = articulo, LineaOcId = lineaOcId, Cantidad = 10m, UbicacionId = bin } },
        }), "B recibe 10/10");
        var entregados = new HashSet<Guid>();
        await EntregarPendientesAsync(app, sender, typeof(ComprasDbContext), desde, entregados);
        await EntregarPendientesAsync(app, sender, typeof(AlmacenDbContext), desde, entregados);

        // Factura en CxP -> fila factura.registrada.v1 en su outbox.
        var iva = Math.Round(subtotal * 0.16m, 2);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(Cfdi(uuid, ProveedorRfc, subtotal, iva, hoy))) { Headers = { ContentType = new("text/xml") } }, "xml", "f.xml" } };
        var cfdiResp = await b.PostAsync("/api/v1/cuentas-por-pagar/cfdis/cargar", form);
        await Ok(cfdiResp, "B carga CFDI");
        var cfdiId = (await J(cfdiResp)).GetProperty("id").GetGuid();
        var fecha = new DateTimeOffset(hoy.Year, hoy.Month, hoy.Day, 12, 0, 0, TimeSpan.Zero);
        var facResp = await b.PostAsJsonAsync("/api/v1/cuentas-por-pagar/facturas", new
        {
            OrdenCompraId = ocId, ProveedorId = Proveedor, SucursalId = TestComprasFixtures.SucursalMid, CfdiRecibidoId = cfdiId, UuidCfdi = uuid,
            FolioProveedor = "F-C18", FechaDocumento = fecha, FechaContabilizacion = fecha, FechaVencimiento = hoy.AddDays(30), Moneda = "MXN",
            Subtotal = subtotal, Descuentos = 0m, ImpuestosTrasladados = iva, Retenciones = 0m, Total = subtotal + iva,
            Lineas = new[] { new { ArticuloId = (Guid?)articulo, Descripcion = "C1.8", Cantidad = 10m, ClaveUnidad = "H87", PrecioUnitario = subtotal / 10m, Importe = subtotal, LineaOcId = (Guid?)lineaOcId } },
        });
        await Ok(facResp, "B captura factura contra OC");
        var facturaId = (await J(facResp)).GetProperty("id").GetGuid();

        // Tick real del OutboxPublisherWorker de CxP con el sender local.
        var worker = new OutboxPublisherWorker<CuentasPorPagarDbContext>(
            app.Services.GetRequiredService<IServiceScopeFactory>(),
            app.Services.GetRequiredService<IOptionsMonitor<OutboxPublisherOptions>>(),
            NullLogger<OutboxPublisherWorker<CuentasPorPagarDbContext>>.Instance);
        IntegrationEventOutboxEntry? fila = null;
        for (var tick = 0; tick < 10 && fila?.PublishedAt is null; tick++)
        {
            await worker.PollOnceAsync(CancellationToken.None);
            fila = await FilaFacturaRegistradaAsync(app, facturaId);
        }

        Assert.NotNull(fila);
        Assert.True(fila!.PublishedAt is not null, $"La fila del outbox de CxP no se entregó: Attempts={fila.Attempts} LastError={fila.LastError}");
        var (cantidad, subEstado) = await FacturacionOcAsync(app, ocId, lineaOcId);
        Assert.Equal(10m, cantidad);
        Assert.NotEqual(SubEstadoFacturacion.SinFactura, subEstado);
        Assert.Equal(1, await MarcasComprasAsync(app, fila.Id, FacturaRegistrada));

        // Re-entrega de la misma fila (p. ej. reintento tras falla de otro consumidor): sin doble efecto.
        var topic = Topic(app, typeof(CuentasPorPagarDbContext));
        await sender.SendAsync(fila, topic, CancellationToken.None);
        Assert.Equal((cantidad, subEstado), await FacturacionOcAsync(app, ocId, lineaOcId));
        Assert.Equal(1, await MarcasComprasAsync(app, fila.Id, FacturaRegistrada));
    }

    // ---------- C1.8-b ----------
    [Fact]
    public async Task C18b_la_misma_fila_entregada_dos_veces_deja_un_solo_efecto_y_una_sola_marca()
    {
        var app = HostConEntregaLocal();
        var sender = app.Services.GetRequiredService<IIntegrationEventBusSender>();
        // Informativo para Compras (marca en eventos_procesados); Almacén y Tesorería no lo consumen.
        const string tipo = "cuentas_por_pagar.estado-cuenta-tc.cerrado.v1";
        var fila = new IntegrationEventOutboxEntry(Guid.CreateVersion7(), tipo, "{}", DateTimeOffset.UtcNow, EmpresaBootstrapId);
        var topic = Topic(app, typeof(CuentasPorPagarDbContext));

        await sender.SendAsync(fila, topic, CancellationToken.None);
        await sender.SendAsync(fila, topic, CancellationToken.None);

        Assert.Equal(1, await MarcasComprasAsync(app, fila.Id, tipo));
    }

    // ---------- C1.8-c ----------
    [Fact]
    public void C18c_con_conexion_a_Service_Bus_la_entrega_local_se_ignora()
    {
        // Ningún HostedService: con conexión (falsa) los listeners y AwDropWorker
        // intentarían abrir Service Bus; aquí solo interesa el registro.
        var app = HostConEntregaLocal(b =>
        {
            b.UseSetting(
                "ServiceBus:ConnectionString",
                "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=dGVzdA==");
            b.ConfigureTestServices(s =>
            {
                foreach (var d in s.Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType?.Name != "GenericWebHostService").ToList())
                {
                    s.Remove(d);
                }
            });
        });

        Assert.IsType<ServiceBusIntegrationEventBusSender>(app.Services.GetRequiredService<IIntegrationEventBusSender>());
        Assert.Empty(app.Services.GetServices<ILocalIntegrationEventConsumer>());
    }

    [Fact]
    public void C18c_en_Production_con_la_entrega_local_activa_el_arranque_falla()
    {
        // Auth FakeForLocalDev (puesto por TestAssemblyInit) no arranca fuera de
        // Development: se cambia solo para esta prueba y se restaura.
        var authPrevio = Environment.GetEnvironmentVariable("Auth__Mode");
        Environment.SetEnvironmentVariable("Auth__Mode", "EntraId");
        try
        {
            using var app = factory.WithWebHostBuilder(b =>
            {
                b.UseEnvironment(Environments.Production);
                b.UseSetting(EntregaLocalIntegrationEventsExtensions.ClaveHabilitada, "true");
            });

            var ex = Record.Exception(() => app.Services);

            Assert.NotNull(ex);
            var mensajes = new List<string>();
            for (var e = ex; e is not null; e = e.InnerException) mensajes.Add(e.Message);
            Assert.Contains(mensajes, m => m.Contains("C1.8") && m.Contains("solo se permite en Development"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__Mode", authPrevio);
        }
    }

    // ================= helpers =================

    private WebApplicationFactory<Program> HostConEntregaLocal(Action<IWebHostBuilder>? extra = null) =>
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
            extra?.Invoke(b);
        });

    private static string Topic(WebApplicationFactory<Program> app, Type ctx) =>
        app.Services.GetRequiredService<IOptionsMonitor<OutboxPublisherOptions>>().Get(ctx.Name).ServiceBusTopicName;

    private static async Task EntregarPendientesAsync(
        WebApplicationFactory<Program> app, IIntegrationEventBusSender sender, Type ctx, DateTimeOffset desde, HashSet<Guid> entregados)
    {
        List<IntegrationEventOutboxEntry> filas;
        using (var scope = app.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = (DbContext)scope.ServiceProvider.GetRequiredService(ctx);
            filas = (await db.Set<IntegrationEventOutboxEntry>().AsNoTracking().Where(e => e.OccurredAt >= desde).ToListAsync())
                .OrderBy(e => e.OccurredAt).ToList();
        }
        var topic = Topic(app, ctx);
        foreach (var fila in filas.Where(f => entregados.Add(f.Id)))
        {
            await sender.SendAsync(fila, topic, CancellationToken.None);
        }
    }

    private static async Task<IntegrationEventOutboxEntry?> FilaFacturaRegistradaAsync(WebApplicationFactory<Program> app, Guid facturaId)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        var id = facturaId.ToString();
        return (await db.Set<IntegrationEventOutboxEntry>().AsNoTracking()
                .Where(e => e.EventType == FacturaRegistrada).ToListAsync())
            .SingleOrDefault(e => e.Payload.Contains(id, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<(decimal Cantidad, SubEstadoFacturacion SubEstado)> FacturacionOcAsync(
        WebApplicationFactory<Program> app, Guid ocId, Guid lineaOcId)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var oc = await db.OrdenesCompra.AsNoTracking().Include(o => o.Lineas).SingleAsync(o => o.Id == ocId);
        return (oc.Lineas.Single(l => l.Id == lineaOcId).CantidadFacturada, oc.SubEstadoFacturacion);
    }

    private static async Task<int> MarcasComprasAsync(WebApplicationFactory<Program> app, Guid eventoId, string tipo)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        return await db.EventosProcesados.CountAsync(e => e.EventoId == eventoId && e.EventoTipo == tipo);
    }
}
