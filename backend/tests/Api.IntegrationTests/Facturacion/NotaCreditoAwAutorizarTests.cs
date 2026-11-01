using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Api.Infrastructure;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Application.Retorno;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// 86e3wjt5r · criterio 4: una NC de A+W «Por autorizar» de 1,500 sobre una factura con saldo de 5,000
/// se autoriza por la API, se timbra (PAC simulado) y por la entrega local de eventos el saldo de la
/// cartera baja 1,500. El evento de retorno lleva el UUID, folio, pedido y versión que
/// <c>AwRetornoDocumentoService</c> escribe en <c>aw_retorno</c> (esa fila se prueba en <c>AwNotasCreditoPgTests</c>).
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class NotaCreditoAwAutorizarTests(WebApplicationFactory<Program> factory)
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");

    [Fact]
    public async Task Autorizar_la_nota_de_1500_la_timbra_baja_el_saldo_y_deja_el_retorno_con_el_UUID()
    {
        await using var app = HostConEntregaLocal();
        var cliente = await ClienteConPermisosAsync(app, PermisosCanonicos.Todos.Select(p => p.Codigo).ToArray());
        var (ncId, carteraId) = await SembrarAsync(app);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/facturacion/notas-credito/{ncId}/autorizar");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var r = await cliente.SendAsync(request);
        var cuerpo = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        var uuid = JsonDocument.Parse(cuerpo).RootElement.GetProperty("uuid").GetString();
        uuid.Should().NotBeNullOrEmpty();

        // Entrega local: el listener de CxC aplica la NC a la cartera.
        for (var tick = 0; tick < 15 && await MontoNcAsync(app, carteraId) == 0m; tick++)
        {
            await TickAsync<FacturacionDbContext>(app);
            await TickAsync<CuentasPorCobrarDbContext>(app);
        }

        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var factura = await scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>().FacturasCartera
            .AsNoTracking().SingleAsync(f => f.Id == carteraId);
        factura.MontoNc.Should().Be(1500m);
        factura.SaldoPendiente.Should().Be(3500m);

        var evento = (await scope.ServiceProvider.GetRequiredService<FacturacionDbContext>().OutboxEntries.AsNoTracking()
            .Where(e => e.EventType == "facturacion.nota-credito.timbrada.v1").ToListAsync())
            .Select(e => JsonSerializer.Deserialize<FacturaTimbradaRetornoPayload>(e.Payload, JsonSerializerOptions.Web)!)
            .Single(p => p.Uuid == uuid);
        (evento.Total, evento.NumeroPedidoAw, evento.VersionPedidoAw).Should().Be((1500m, "AW-NC-CIERRE", 3L));
        evento.Folio.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Sin_el_permiso_de_autorizar_la_API_responde_403_y_la_nota_sigue_por_autorizar()
    {
        await using var app = HostConEntregaLocal();
        var cliente = await ClienteConPermisosAsync(app, PermisosCanonicos.FacturacionNotasCreditoLeer);
        var (ncId, _) = await SembrarAsync(app);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/facturacion/notas-credito/{ncId}/autorizar");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        (await cliente.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        (await scope.ServiceProvider.GetRequiredService<FacturacionDbContext>().NotasCredito.AsNoTracking()
            .SingleAsync(n => n.Id == ncId)).Estado.Should().Be(EstadoTimbrado.PorAutorizar);
    }

    private static async Task<decimal> MontoNcAsync(WebApplicationFactory<Program> app, Guid carteraId)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>().FacturasCartera
            .AsNoTracking().Where(f => f.Id == carteraId).Select(f => f.MontoNc).SingleAsync();
    }

    private static Task TickAsync<TContext>(WebApplicationFactory<Program> app) where TContext : DbContext =>
        new OutboxPublisherWorker<TContext>(
            app.Services.GetRequiredService<IServiceScopeFactory>(),
            app.Services.GetRequiredService<IOptionsMonitor<OutboxPublisherOptions>>(),
            NullLogger<OutboxPublisherWorker<TContext>>.Instance).PollOnceAsync(CancellationToken.None);

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
            s.RemoveAll<IPeriodoContablePort>(); s.AddSingleton<IPeriodoContablePort, Periodo>();
        });
    });

    /// <summary>Factura PPD timbrada de 5,000 (cartera con ese saldo) y la NC de A+W de 1,500 «Por autorizar».</summary>
    private static async Task<(Guid NcId, Guid CarteraId)> SembrarAsync(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var fac = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var cxc = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente ficticio NC A+W", "601", "97000", "G03", "MEX", false);
        var emisor = new DatosFiscalesEmisor("BBB010101BBB", "Emisor ficticio NC A+W", "601", "97000");
        var f = FacturaVenta.CrearBorrador(Empresa, $"NCAW-{Guid.NewGuid():N}", 1, Guid.NewGuid(), null, null,
            receptor, emisor, "PPD", "99", "MXN", null, hoy.Year, hoy.Month, canalVentaId: 7,
            comportamientoFiscal: ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "01010101", "Prueba NC A+W", "E48", 1m, 5000m, 0m, "01", null, null, null);
        f.RecalcularTotales(); f.MarcarTimbradoEnProceso();
        var uuidFactura = Guid.NewGuid().ToString();
        f.MarcarTimbrado(uuidFactura, null, null, null, DateTimeOffset.UtcNow, null, null);
        fac.FacturasVenta.Add(f);
        var nc = NotaCredito.CrearDesdeAw(Empresa, $"NCAW-{Guid.NewGuid():N}"[..20], 1, f.SucursalId, null, receptor, emisor, "99", "MXN", null,
            hoy.Year, hoy.Month, 7, MotivoNotaCredito.Bonificacion, f.Id, 1500m, 0.16m, "Descuento",
            $"NC-{Guid.NewGuid():N}"[..12], 1, "AW-NC-CIERRE", 3);
        nc.AgregarRelacion(uuidFactura, MotivoNotaCredito.Bonificacion.ClaveTipoRelacion());
        fac.NotasCredito.Add(nc);
        var cartera = FacturaCartera.Crear(Empresa, f.Id, Guid.NewGuid(), "AAA010101AAA", "Cliente ficticio NC A+W", uuidFactura,
            $"NCAW-{Guid.NewGuid():N}"[..20], 5000m, "MXN", "PPD", DateTimeOffset.UtcNow.AddDays(-5), DateTimeOffset.UtcNow.AddDays(25));
        cxc.FacturasCartera.Add(cartera);
        await fac.SaveChangesAsync();
        await cxc.SaveChangesAsync();
        return (nc.Id, cartera.Id);
    }

    private sealed class Periodo : IPeriodoContablePort
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
