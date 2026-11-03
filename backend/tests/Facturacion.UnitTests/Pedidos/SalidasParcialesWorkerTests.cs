using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Facturacion.UnitTests.Pedidos;

/// <summary>FAC-08: el worker registra las salidas del lector y un reenvío no duplica. Datos ficticios.</summary>
public sealed class SalidasParcialesWorkerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 9, 16, 0, 0, TimeSpan.Zero);

    private sealed class LectorFalso(params SalidaParcial[] salidas) : ISalidasParcialesReader
    {
        public Task<IReadOnlyList<SalidaParcial>> LeerPendientesAsync(int max, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SalidaParcial>>(salidas);
    }

    [Fact]
    public async Task Registra_las_salidas_del_lector_y_al_reentregarlas_no_las_duplica()
    {
        var empresaId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        using (var seed = new FacturacionDbContext(
            new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(dbName).Options, new FakeEmpresaContext(empresaId)))
        {
            var pedido = PedidoFacturable.ImportarDesdeAw(empresaId, "10425068", Guid.NewGuid(), Guid.NewGuid(), "Cliente Obra",
                (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
            pedido.AgregarLinea(null, "Vidrio templado 6 mm", "30171500", "H87", 100m, 10m, 0m, false);
            pedido.RecalcularTotal();
            seed.PedidosFacturables.Add(pedido);
            await seed.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(empresaId));
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddDbContext<FacturacionDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IClock>(new FakeClock(Ahora));
        services.AddSingleton<ISalidasParcialesReader>(new LectorFalso(
            new SalidaParcial("10425068", OrigenSalidaFacturable.ProductoTerminado, "SAL-1", [(1, 60m)]),
            new SalidaParcial("10425068", OrigenSalidaFacturable.ProductoTerminado, "SAL-2", [(1, 40m)]),
            new SalidaParcial("10425068", OrigenSalidaFacturable.ProductoTerminado, "SAL-3", [(1, 1m)])));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Millet.Facturacion.Application.AssemblyMarker).Assembly));
        services.AddOptions<SalidasParcialesOptions>();
        var sp = services.BuildServiceProvider();
        var worker = new SalidasParcialesWorker(sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptionsMonitor<SalidasParcialesOptions>>(), NullLogger<SalidasParcialesWorker>.Instance);

        var primera = await worker.TickAsync(CancellationToken.None);
        var segunda = await worker.TickAsync(CancellationToken.None);

        primera.Should().Be(2, "la tercera salida rebasa lo pedido y se omite");
        segunda.Should().Be(0);
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        (await db.SalidasFacturables.OrderBy(s => s.NumeroParcialidad).Select(s => s.Total).ToListAsync())
            .Should().Equal(600m, 400m);
    }
}
