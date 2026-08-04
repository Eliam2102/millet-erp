using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.UnitTests.Cancelaciones;

public sealed class CancelacionPollerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>El SAT reporta el CFDI como NO vigente (cancelación procesada).</summary>
    private sealed class FakeFiscalNoVigente : ICfdiTimbradoPort
    {
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct) => throw new NotImplementedException();
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) => throw new NotImplementedException();
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            Task.FromResult(new EstatusCfdiResultado(false, "Cancelado", false, "Cancelado"));
    }

    [Fact]
    public async Task Poller_resuelve_EnProceso_cuando_el_SAT_reporta_no_vigente()
    {
        var empresaId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(empresaId));
        services.AddDbContext<FacturacionDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<ICfdiTimbradoPort>(new FakeFiscalNoVigente());
        services.AddSingleton<IClock>(new FakeClock(Ahora));
        services.AddSingleton<Millet.SharedKernel.Application.IIntegrationEventPublisher>(new Millet.Facturacion.UnitTests.TestDoubles.FakeIntegrationEventPublisher());
        services.AddOptions<CancelacionSatPollerOptions>();
        var sp = services.BuildServiceProvider();

        Guid facturaId, pedidoId;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var pedido = PedidoFacturable.ImportarDesdeAw(empresaId, "AW-1", Guid.NewGuid(), Guid.NewGuid(),
                "Cliente", (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
            var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
            var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null, receptor,
                new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "03", "MXN", null, 2026, 5,
                (short)1, ComportamientoFiscal.MostradorInmediato, pedido.Id, null, null, false);
            fv.AgregarLinea(null, "01010101", "P", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
            fv.RecalcularTotales();
            fv.MarcarTimbradoEnProceso();
            fv.MarcarTimbrado("11111111-1111-1111-1111-111111111111", null, null, null, Ahora, null, null);
            fv.MarcarCancelacionPendiente();
            pedido.MarcarFacturado(fv.Id);
            var solicitud = SolicitudCancelacion.Crear(empresaId, fv.Id, "02", null, Ahora);
            solicitud.MarcarEnProceso("EnProceso");
            db.PedidosFacturables.Add(pedido);
            db.FacturasVenta.Add(fv);
            db.SolicitudesCancelacion.Add(solicitud);
            await db.SaveChangesAsync();
            facturaId = fv.Id;
            pedidoId = pedido.Id;
        }

        var worker = new CancelacionSatPollerWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptionsMonitor<CancelacionSatPollerOptions>>(),
            NullLogger<CancelacionSatPollerWorker>.Instance);

        var resueltas = await worker.TickAsync(CancellationToken.None);

        resueltas.Should().Be(1);
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            (await db.FacturasVenta.FirstAsync(f => f.Id == facturaId)).Estado.Should().Be(EstadoTimbrado.Cancelado);
            (await db.PedidosFacturables.FirstAsync(p => p.Id == pedidoId)).Estado.Should().Be(EstadoPedidoFacturable.Importado);
            (await db.SolicitudesCancelacion.SingleAsync()).Estado.Should().Be(EstadoSolicitudCancelacion.Aceptada);
        }
    }

    [Fact]
    public async Task Poller_no_resuelve_si_el_SAT_reporta_vigente()
    {
        var empresaId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(empresaId));
        services.AddDbContext<FacturacionDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<ICfdiTimbradoPort>(new FakeFiscalApiClient()); // ConsultarEstatus → vigente
        services.AddSingleton<IClock>(new FakeClock(Ahora));
        services.AddSingleton<Millet.SharedKernel.Application.IIntegrationEventPublisher>(new Millet.Facturacion.UnitTests.TestDoubles.FakeIntegrationEventPublisher());
        services.AddOptions<CancelacionSatPollerOptions>();
        var sp = services.BuildServiceProvider();

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
            var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null, receptor,
                new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "03", "MXN", null, 2026, 5,
                (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
            fv.AgregarLinea(null, "01010101", "P", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
            fv.RecalcularTotales();
            fv.MarcarTimbradoEnProceso();
            fv.MarcarTimbrado("11111111-1111-1111-1111-111111111111", null, null, null, Ahora, null, null);
            fv.MarcarCancelacionPendiente();
            var solicitud = SolicitudCancelacion.Crear(empresaId, fv.Id, "02", null, Ahora);
            solicitud.MarcarEnProceso("EnProceso");
            db.FacturasVenta.Add(fv);
            db.SolicitudesCancelacion.Add(solicitud);
            await db.SaveChangesAsync();
        }

        var worker = new CancelacionSatPollerWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptionsMonitor<CancelacionSatPollerOptions>>(),
            NullLogger<CancelacionSatPollerWorker>.Instance);

        var resueltas = await worker.TickAsync(CancellationToken.None);

        resueltas.Should().Be(0);
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            (await db.SolicitudesCancelacion.SingleAsync()).Estado.Should().Be(EstadoSolicitudCancelacion.EnProceso);
        }
    }
}
