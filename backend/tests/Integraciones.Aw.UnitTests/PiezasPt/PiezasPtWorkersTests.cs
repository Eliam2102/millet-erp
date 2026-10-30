using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure;
using Millet.SharedKernel.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Infrastructure.PiezasPt;
using Millet.ProductoTerminado.Infrastructure.Persistence;

namespace Millet.Integraciones.Aw.UnitTests.PiezasPt;
public sealed class PiezasPtWorkersTests
{
    [Theory][InlineData(false, true)][InlineData(true, false)]
    public async Task Disabled_or_missing_company_never_opens_a_scope(bool enabled, bool company)
    {
        using var worker = new PiezasPtEntradaWorker(new ForbiddenScope(), Options.Create(new PiezasPtOptions { EntradaHabilitada = enabled, EmpresaId = company ? Guid.NewGuid() : null }), NullLogger<PiezasPtEntradaWorker>.Instance);
        Assert.Equal(0, await worker.RunOnceAsync(default));
    }
    [Theory][InlineData(false, "Development")][InlineData(false, "Production")][InlineData(true, "Development")]
    public void Real_requires_contractSigned_contract_and_demo_is_absent_in_production(bool contractSigned, string environment)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["IntegracionesAw:PiezasPt:EntradaHabilitada"] = "true", ["IntegracionesAw:PiezasPt:ContratoI134Firmado"] = contractSigned.ToString(),
            ["ConnectionStrings:AwProduccionDb"] = "Server=localhost;Database=alcimdb;Integrated Security=true;Encrypt=true;TrustServerCertificate=false"
        }).Build();
        using var sp = new ServiceCollection().AddAwPiezasPt(config, new EnvironmentStub(environment)).BuildServiceProvider();
        Assert.Equal(contractSigned, sp.GetKeyedService<IPiezasPtReader>("Real") is not null);
        Assert.Equal(environment != "Production", sp.GetKeyedService<IPiezasPtReader>("Demo") is not null);
    }
    [Fact]
    public async Task Empty_station_list_never_opens_origin_and_event_key_is_stable()
    {
        var reader = new PiezasPtReader(() => throw new InvalidOperationException("No connection allowed"), false);
        Assert.Empty(await reader.ReadAsync(new PtWatermark(), 200, [], default));
        var row = new PtSourceScan("ET1", "900101", 1, 1, new DateTime(2026, 10, 30, 12, 0, 0), 9069);
        Assert.Equal("ET1|9069|2026-10-30T12:00:00.0000000", row.SourceEventId);
        Assert.NotEqual(row.SourceEventId, (row with { Station = 9070 }).SourceEventId);
    }
    [Fact]
    public async Task Cursor_ties_unknown_station_and_saved_postponement_are_processed_without_source_ack()
    {
        var company = Guid.NewGuid(); var branch = Guid.NewGuid(); var clock = new ClockStub(); var source = new OrderStub(branch);
        using var db = new ProductoTerminadoDbContext(new DbContextOptionsBuilder<ProductoTerminadoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new CompanyStub());
        using var sp = new ServiceCollection().AddSingleton<IClock>(clock).AddSingleton<ISucursalPorClaveAwResolver>(new BranchStub(branch))
            .AddSingleton(new PtScanHandler(db, source, clock, Options.Create(new PtScanOptions()))).BuildServiceProvider();
        var options = Options.Create(new PiezasPtOptions { EmpresaId = company, BatchSize = 1, EstacionesPt = [new(9069, "DEMO")] });
        var reader = new ReaderStub();
        using var worker = new PiezasPtEntradaWorker(sp.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<PiezasPtEntradaWorker>.Instance);
        await worker.ProcessAsync(sp, db, "Demo", reader, default); // pedido inexistente; cursor persiste
        Assert.Equal("Pospuesto", (await db.Scans.SingleAsync()).Resultado);
        source.Available = true; clock.Now = clock.Now.AddMinutes(1);
        await worker.ProcessAsync(sp, db, "Demo", reader, default); // reintento local + siguiente fila de la misma hora
        Assert.Equal(2, await db.Pieces.CountAsync()); Assert.Equal(2, await db.OutboxEntries.CountAsync());
        await worker.ProcessAsync(sp, db, "Demo", reader, default);
        Assert.Equal("PT_SUCURSAL_SIN_CLAVE_AW", (await db.Scans.SingleAsync(x => x.OrigenEventoId.StartsWith("c|"))).Motivo);
        Assert.Equal("c", (await db.Watermarks.SingleAsync()).Barcode);
        Assert.Equal(3, await db.Scans.CountAsync()); Assert.Equal(2, await db.Movements.CountAsync());
    }
    private sealed class ReaderStub : IPiezasPtReader
    {
        public Task<IReadOnlyList<PtSourceScan>> ReadAsync(PtWatermark cursor, int batchSize, IReadOnlyList<PtStation> stations, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PtSourceScan>>(new[] {
                new PtSourceScan("a", "900101", 1, 1, new DateTime(2026,10,30,12,0,0), 9069),
                new PtSourceScan("b", "900101", 1, 2, new DateTime(2026,10,30,12,0,0), 9069),
                new PtSourceScan("c", "900101", 1, 3, new DateTime(2026,10,30,12,0,0), 9999)
            }.Where(r => r.Time > cursor.Timestamp || r.Time == cursor.Timestamp && string.CompareOrdinal(r.Barcode, cursor.Barcode) > 0).Take(batchSize).ToArray());
    }
    private sealed class OrderStub(Guid branch) : IPedidosPtReadPort
    {
        public bool Available;
        public Task<PtOrderSource?> GetAsync(Guid empresaId, string orderNumber, CancellationToken ct) => Task.FromResult<PtOrderSource?>(Available
            ? new(Guid.NewGuid(), orderNumber, branch, "Importado", null, Guid.NewGuid(), "MXN", 300, 1, [new(1, null, "Glass", 3, "H87")]) : null);
    }
    private sealed class ClockStub : IClock { public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-30T12:00:00Z"); public DateTimeOffset UtcNow => Now; }
    private sealed class BranchStub(Guid branch) : ISucursalPorClaveAwResolver { public Task<Guid?> ResolverAsync(string key, CancellationToken cancellationToken) => Task.FromResult<Guid?>(branch); }
    private sealed class CompanyStub : ICurrentEmpresaContext { public Guid? Current => null; public bool IsBypassed => true; public IDisposable Bypass() => throw new NotSupportedException(); }
    private sealed class ForbiddenScope : IServiceScopeFactory { public IServiceScope CreateScope() => throw new InvalidOperationException("No scope allowed"); }
    private sealed class EnvironmentStub(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name; public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = ""; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
