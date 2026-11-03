using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Administracion.Application.Series;
using Millet.Api.Infrastructure;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.CuentasPorCobrar;

/// <summary>
/// F1-CXC-05 Parte 2 · compensación de punta a punta con entrega local de eventos (C1.8, sin Service Bus): anticipo de
/// 1,160 y factura PPD timbrada de 2,320 en cartera → POST /anticipos/{id}/aplicar por 580 → NC 07 timbrada (PAC simulado)
/// → el evento llega a CxC solo con un tick del outbox → la factura baja su saldo a 1,740 con un movimiento NotaCredito
/// ligado a la NC, y el anticipo queda con 580. Datos ficticios.
/// </summary>
public sealed class CompensacionAnticipoEntregaLocalTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly DatosFiscalesReceptor Receptor = new("AAA010101AAA", "Cliente ficticio CXC-05 P2", "601", "97000", "G03", "MEX", false);
    private static readonly DatosFiscalesEmisor Emisor = new("BBB010101BBB", "Emisor ficticio CXC-05 P2", "601", "97000");

    [Fact]
    public async Task Aplicar_anticipo_a_factura_timbrada_timbra_la_NC_07_y_CxC_baja_el_saldo_con_el_movimiento_ligado()
    {
        await using var app = HostConEntregaLocal();
        var client = await LoginAsync(app);
        var clienteId = Guid.NewGuid();
        var (anticipoId, facturaId, carteraId) = await SembrarAsync(app, clienteId);

        var respuesta = await client.PostAsJsonAsync($"/api/v1/facturacion/anticipos/{anticipoId}/aplicar",
            new { facturaVentaId = facturaId, importe = 580m });
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        var ncId = JsonDocument.Parse(cuerpo).RootElement.GetProperty("notaCreditoId").GetGuid();

        // Facturación: NC de amortización timbrada con relación 07 al anticipo y a la factura; el anticipo baja su saldo.
        using (var scope = app.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var fac = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var nc = await fac.NotasCredito.AsNoTracking().Include(n => n.Relaciones).SingleAsync(n => n.Id == ncId);
            nc.Estado.Should().Be(EstadoTimbrado.Timbrado);
            nc.Motivo.Should().Be(MotivoNotaCredito.Amortizacion);
            nc.FacturaRelacionadaId.Should().Be(facturaId);
            nc.Relaciones.Should().HaveCount(2).And.OnlyContain(r => r.TipoRelacion == "07");
            (await fac.Anticipos.AsNoTracking().SingleAsync(a => a.Id == anticipoId)).Saldo.Should().Be(580m);
        }

        // CxC: el evento de la NC llega por la entrega local y baja el saldo de la factura con el rastro a la NC.
        var cartera = await EsperarAsync(app, async sp =>
        {
            var f = await sp.GetRequiredService<CuentasPorCobrarDbContext>().FacturasCartera.AsNoTracking()
                .SingleAsync(x => x.Id == carteraId);
            return f.MontoNc > 0 ? f : null;
        }, "la NC 07 descontada en cartera");
        cartera.MontoNc.Should().Be(580m);
        cartera.SaldoPendiente.Should().Be(1740m);
        cartera.Estado.Should().Be(EstadoFacturaCartera.Parcial);

        using var verificar = app.Services.CreateScope();
        using var empresa = verificar.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var movimiento = await verificar.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>().MovimientosCartera
            .AsNoTracking().SingleAsync(m => m.FacturaCarteraId == carteraId);
        movimiento.Tipo.Should().Be(TipoMovimientoCartera.NotaCredito);
        movimiento.OrigenComprobanteId.Should().Be(ncId, "el movimiento conserva el origen: la NC de la compensación");
        movimiento.Importe.Should().Be(580m);
    }

    [Fact]
    public async Task No_aplica_mas_de_lo_que_la_factura_tiene_pendiente_en_cartera()
    {
        await using var app = HostConEntregaLocal();
        var client = await LoginAsync(app);
        var (anticipoId, facturaId, carteraId) = await SembrarAsync(app, Guid.NewGuid());
        using (var scope = app.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var cxc = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
            (await cxc.FacturasCartera.SingleAsync(f => f.Id == carteraId)).AplicarPago(2000m);
            await cxc.SaveChangesAsync();
        }

        var respuesta = await client.PostAsJsonAsync($"/api/v1/facturacion/anticipos/{anticipoId}/aplicar",
            new { facturaVentaId = facturaId, importe = 580m });

        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await respuesta.Content.ReadAsStringAsync()).Should().Contain("ANTICIPO_EXCEDE_SALDO_FACTURA");
    }

    // ================= datos =================

    private static async Task<(Guid AnticipoId, Guid FacturaId, Guid CarteraId)> SembrarAsync(WebApplicationFactory<Program> app, Guid clienteId)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var fac = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var cxc = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        var ahora = DateTimeOffset.UtcNow;
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var sucursal = TestComprasFixtures.SucursalMid;

        var anticipoId = Guid.CreateVersion7();
        var facturaAnticipo = FacturaAnticipo.CrearBorrador(Empresa, $"P2A-{sufijo}", 1, sucursal, null, null, Receptor, Emisor,
            "03", "MXN", null, ahora.Year, ahora.Month, TipoAnticipo.ClientesMxp, null, anticipoId, 1000m, 0.16m, "Anticipo ficticio", 7);
        facturaAnticipo.MarcarTimbradoEnProceso();
        facturaAnticipo.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, ahora.AddMinutes(-10), null, null);
        var anticipo = Anticipo.Crear(Empresa, clienteId, Receptor.Rfc, TipoAnticipo.ClientesMxp, "MXN",
            facturaAnticipo.Total, facturaAnticipo.Id, pedidoOrigenRef: "AW-P2", id: anticipoId);

        var factura = FacturaVenta.CrearBorrador(Empresa, $"P2F-{sufijo}", 1, sucursal, null, null, Receptor, Emisor,
            "PPD", "99", "MXN", null, ahora.Year, ahora.Month, 7, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        factura.AgregarLinea(null, "01010101", "Vidrio ficticio", "H87", 1m, 2000m, 0m, "02", 0.16m, null, null);
        factura.RecalcularTotales();
        factura.MarcarTimbradoEnProceso();
        var uuid = Guid.NewGuid().ToString();
        factura.MarcarTimbrado(uuid, null, null, null, ahora.AddMinutes(-5), null, null);

        fac.AddRange(facturaAnticipo, anticipo, factura);
        var cartera = FacturaCartera.Crear(Empresa, factura.Id, clienteId, Receptor.Rfc, Receptor.Nombre, uuid,
            factura.Folio, factura.Total, "MXN", "PPD", ahora.AddMinutes(-5), ahora.AddDays(30));
        cxc.FacturasCartera.Add(cartera);
        await fac.SaveChangesAsync();
        await cxc.SaveChangesAsync();
        return (anticipoId, factura.Id, cartera.Id);
    }

    // ================= host con entrega local (patrón C74CicloCompletoEntregaLocalTests) =================

    private WebApplicationFactory<Program> HostConEntregaLocal() => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting(EntregaLocalIntegrationEventsExtensions.ClaveHabilitada, "true");
        b.ConfigureTestServices(s =>
        {
            // Sin outbox de fondo (los ticks son explícitos) ni listeners de Service Bus.
            foreach (var d in s.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType is { } t
                         && ((t.IsGenericType && t.GetGenericTypeDefinition() == typeof(OutboxPublisherWorker<>))
                             || t.Name.EndsWith("EventListenerWorker", StringComparison.Ordinal))).ToList())
                s.Remove(d);
            s.RemoveAll<ICfdiTimbradoPort>(); s.AddSingleton<ICfdiTimbradoPort, Pac>();
            s.RemoveAll<ICfdiRepositorioPort>(); s.AddSingleton<ICfdiRepositorioPort, Repositorio>();
            s.RemoveAll<Millet.Facturacion.Domain.Ports.IPeriodoContablePort>();
            s.AddSingleton<Millet.Facturacion.Domain.Ports.IPeriodoContablePort, PeriodoAbierto>();
            s.RemoveAll<IRequestHandler<ReservarFolioCommand, ReservarFolioResponse>>();
            s.AddSingleton<IRequestHandler<ReservarFolioCommand, ReservarFolioResponse>, Folio>();
        });
    });

    /// <summary>Ticks del outbox de Facturación y CxC hasta que la condición se cumple (máx. 10).</summary>
    private static async Task<T> EsperarAsync<T>(WebApplicationFactory<Program> app, Func<IServiceProvider, Task<T?>> condicion, string que)
        where T : class
    {
        for (var tick = 0; tick < 10; tick++)
        {
            using (var scope = app.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                if (await condicion(scope.ServiceProvider) is { } listo) return listo;
            }
            await TickAsync<FacturacionDbContext>(app);
            await TickAsync<CuentasPorCobrarDbContext>(app);
        }
        throw new Xunit.Sdk.XunitException($"No llegó: {que} (10 ticks del outbox con entrega local).");
    }

    private static Task TickAsync<TContext>(WebApplicationFactory<Program> app) where TContext : DbContext =>
        new OutboxPublisherWorker<TContext>(
            app.Services.GetRequiredService<IServiceScopeFactory>(),
            app.Services.GetRequiredService<IOptionsMonitor<OutboxPublisherOptions>>(),
            NullLogger<OutboxPublisherWorker<TContext>>.Instance).PollOnceAsync(CancellationToken.None);

    private sealed class Folio : IRequestHandler<ReservarFolioCommand, ReservarFolioResponse>
    {
        public Task<ReservarFolioResponse> Handle(ReservarFolioCommand request, CancellationToken cancellationToken) =>
            Task.FromResult(new ReservarFolioResponse($"P2N-{Guid.NewGuid():N}"[..20], 1, ""));
    }

    private sealed class PeriodoAbierto : Millet.Facturacion.Domain.Ports.IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class Pac : ICfdiTimbradoPort
    {
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken cancellationToken) =>
            Task.FromResult(new TimbradoResultado(
                TimbradoEstado.Timbrado, Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow, null, "<prueba-ficticia/>", null, null));
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Repositorio : ICfdiRepositorioPort
    {
        public Task<Guid> GuardarAsync(CfdiArchivoNuevo a, CancellationToken cancellationToken) => Task.FromResult(Guid.NewGuid());
        public Task<CfdiArchivoLeido?> ObtenerAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<CfdiArchivoLeido?>(null);
        public Task<CfdiArchivoLeido?> ObtenerPorUuidAsync(string uuid, CancellationToken cancellationToken) => Task.FromResult<CfdiArchivoLeido?>(null);
    }
}
