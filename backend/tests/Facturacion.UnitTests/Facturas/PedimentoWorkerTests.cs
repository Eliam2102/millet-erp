using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Facturacion.UnitTests.Facturas;

public sealed class PedimentoWorkerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeReader(PedimentoSalida? salida) : ISalidasPedimentosReader
    {
        public Task<IReadOnlyList<PedimentoSalida>> LeerPendientesAsync(int max, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PedimentoSalida>>(salida is null ? [] : [salida]);
    }

    [Fact]
    public async Task Worker_aplica_pedimento_y_timbra_la_factura_retenida()
    {
        var empresaId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        // Sembrar una factura PendientePedimento.
        Guid facturaId;
        using (var seedDb = new FacturacionDbContext(
            new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(dbName).Options,
            new FakeEmpresaContext(empresaId)))
        {
            var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
            var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null, receptor,
                new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 5,
                (short)8, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
            fv.AgregarLinea(null, "01010101", "Importado", "H87", 1m, 100m, 0m, "02", 0.16m, null, null, requierePedimento: true);
            fv.RecalcularTotales();
            fv.MarcarPendientePedimento();
            seedDb.FacturasVenta.Add(fv);
            await seedDb.SaveChangesAsync();
            facturaId = fv.Id;
        }

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(empresaId));
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddDbContext<FacturacionDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IPeriodoContablePort>(new FakePeriodoContablePort());
        services.AddSingleton<ICfdiTimbradoPort>(new FakeFiscalApiClient());
        services.AddSingleton<ICfdiRepositorioPort>(new FakeCfdiRepositorioPort());
        services.AddSingleton<IClock>(new FakeClock(Ahora));
        services.AddSingleton<ISalidasPedimentosReader>(new FakeReader(
            new PedimentoSalida(facturaId, "15  47  3001  0001234", new DateOnly(2026, 5, 20), "ID-1")));
        services.AddSingleton<Millet.SharedKernel.Application.IIntegrationEventPublisher>(new Millet.Facturacion.UnitTests.TestDoubles.FakeIntegrationEventPublisher());
        services.AddScoped<Millet.Facturacion.Domain.Ports.IContabilidadAsientoPort>(_ => new Millet.Facturacion.UnitTests.TestDoubles.FakeContabilidadAsientoPort());
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(
            typeof(Millet.Facturacion.Application.AssemblyMarker).Assembly));
        services.AddOptions<PedimentoSalidasOptions>();
        var sp = services.BuildServiceProvider();

        var worker = new PedimentoSalidasWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptionsMonitor<PedimentoSalidasOptions>>(),
            NullLogger<PedimentoSalidasWorker>.Instance);

        var aplicados = await worker.TickAsync(CancellationToken.None);

        aplicados.Should().Be(1);
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            (await db.FacturasVenta.FirstAsync(f => f.Id == facturaId)).Estado.Should().Be(EstadoTimbrado.Timbrado);
        }
    }
}
