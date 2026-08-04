using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.UnitTests.Timbrado;

/// <summary>
/// Tests del <see cref="TimbradoPendienteWorker"/> (F12-PR2): comprobantes
/// atascados en TimbradoEnProceso más allá del umbral pasan a
/// TimbradoFallido(PAC_TIMEOUT) — corregible; los recientes no se tocan.
/// </summary>
public sealed class TimbradoPendienteWorkerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    private static FacturaVenta Atascada(Guid empresaId)
    {
        var fv = FacturaVenta.CrearBorrador(
            empresaId, $"F-{Guid.NewGuid():N}"[..12], 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"),
            "PUE", "01", "MXN", null, 2026, 7,
            (short)8, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "Prod", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        return fv;
    }

    private static (TimbradoPendienteWorker Worker, ServiceProvider Sp) Armar(
        string dbName, Guid empresaId, DateTimeOffset ahora)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(empresaId));
        services.AddDbContext<FacturacionDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IClock>(new FakeClock(ahora));
        services.AddOptions<TimbradoPendienteOptions>();
        var sp = services.BuildServiceProvider();

        var worker = new TimbradoPendienteWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptionsMonitor<TimbradoPendienteOptions>>(),
            NullLogger<TimbradoPendienteWorker>.Instance);
        return (worker, sp);
    }

    [Fact]
    public async Task Atascado_mas_alla_del_umbral_pasa_a_fallido_pac_timeout()
    {
        var empresaId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        Guid facturaId;
        using (var seedDb = new FacturacionDbContext(
            new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(dbName).Options,
            new FakeEmpresaContext(empresaId)))
        {
            var fv = Atascada(empresaId);
            seedDb.FacturasVenta.Add(fv);
            // La auditoría real la setea el interceptor de Program (ausente en
            // in-memory) — fijar UpdatedAt explícito: atascado hace 1 h.
            seedDb.Entry(fv).Property(x => x.UpdatedAt).CurrentValue = Ahora.AddHours(-1);
            await seedDb.SaveChangesAsync();
            facturaId = fv.Id;
        }

        var (worker, sp) = Armar(dbName, empresaId, Ahora);

        var marcados = await worker.TickAsync(CancellationToken.None);

        marcados.Should().Be(1);
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var factura = await db.FacturasVenta.FirstAsync(f => f.Id == facturaId);
        factura.Estado.Should().Be(EstadoTimbrado.TimbradoFallido);
        factura.TimbradoErrorCodigo.Should().Be("PAC_TIMEOUT");
        factura.TimbradoErrorMensaje.Should().Contain(factura.Folio); // instrucción de verificación
    }

    [Fact]
    public async Task Reciente_dentro_del_umbral_no_se_toca()
    {
        var empresaId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        using (var seedDb = new FacturacionDbContext(
            new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(dbName).Options,
            new FakeEmpresaContext(empresaId)))
        {
            var fv = Atascada(empresaId);
            seedDb.FacturasVenta.Add(fv);
            // Reciente: 5 min < umbral default (30 min) → no se toca.
            seedDb.Entry(fv).Property(x => x.UpdatedAt).CurrentValue = Ahora.AddMinutes(-5);
            await seedDb.SaveChangesAsync();
        }

        var (worker, sp) = Armar(dbName, empresaId, Ahora);

        var marcados = await worker.TickAsync(CancellationToken.None);

        marcados.Should().Be(0);
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        (await db.FacturasVenta.SingleAsync()).Estado.Should().Be(EstadoTimbrado.TimbradoEnProceso);
    }
}
