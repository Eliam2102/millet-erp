using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Xunit.Abstractions;

namespace Millet.ProductoTerminado.UnitTests;
public sealed class PtScanTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Same_event_reuses_result_and_distinct_event_is_duplicate()
    {
        using var s = new Scenario(); var input = s.Input();
        var first = await s.Handler.Handle(input, default);
        Assert.Equal(first, await s.Handler.Handle(input, default));
        var duplicate = await s.Handler.Handle(input with { SourceEventId = "another" }, default);
        Assert.Equal("Duplicado", duplicate.Result); Assert.Equal(first.PieceId, duplicate.PieceId);
        Assert.Single(s.Db.Pieces); Assert.Single(s.Db.Movements); Assert.Single(s.Db.OutboxEntries);
    }
    [Fact]
    public async Task Two_pieces_keep_station_branch_scan_time_and_status69()
    {
        using var s = new Scenario(); var input = s.Input();
        await s.Handler.Handle(input, default); await s.Handler.Handle(input with { SourceEventId = "second", PieceNumber = 2 }, default);
        Assert.Equal(2, s.Db.Pieces.Count()); Assert.Equal(2, s.Db.OutboxEntries.Count());
        Assert.All(s.Db.Pieces, p => { Assert.Equal(input.BranchId, p.SucursalId); Assert.Equal(input.ScannedAt, p.EscaneadaAt); Assert.Equal(69, p.EstatusAw); Assert.Equal("EnPT", p.Estado); });
    }
    [Fact]
    public async Task Missing_order_is_postponed_then_enters_once_from_saved_scan()
    {
        using var s = new Scenario(); var source = s.Source.Order; s.Source.Order = null;
        var first = await s.Handler.Handle(s.Input(), default); Assert.Equal("Pospuesto", first.Result);
        s.Source.Order = source; var scan = await s.Db.Scans.SingleAsync(); await s.Handler.ApplyAsync(scan, default); await s.Db.SaveChangesAsync();
        Assert.Equal("Ingresada", scan.Resultado); Assert.Single(s.Db.Pieces);
        Assert.Equal("Ingresada", (await s.Handler.Handle(s.Input(), default)).Result); Assert.Single(s.Db.Movements);
    }
    [Theory]
    [InlineData(4, 1, true, "PT_PIEZA_EXCEDE_POSICION")]
    [InlineData(1, 2, true, "PT_POSICION_INEXISTENTE")]
    [InlineData(null, 1, true, "PT_IDENTIDAD_INCOMPLETA")]
    [InlineData(1, 1, false, "PT_SUCURSAL_SIN_CLAVE_AW")]
    public async Task Invalid_scan_is_exception_without_stock(int? piece, int position, bool branch, string code)
    {
        using var s = new Scenario(); var result = await s.Handler.Handle(s.Input() with { PieceNumber = piece, Position = position, BranchId = branch ? s.Branch : null }, default);
        Assert.Equal("Excepcion", result.Result); Assert.Equal(code, result.Reason); Assert.Empty(s.Db.Pieces); Assert.Empty(s.Db.OutboxEntries);
    }
    [Fact]
    public async Task Postponement_expires_after_24_hours_and_cancelled_order_is_rejected()
    {
        using var s = new Scenario(); var source = s.Source.Order!; s.Source.Order = null;
        await s.Handler.Handle(s.Input(), default); s.Clock.Now = s.Clock.Now.AddHours(24);
        var scan = await s.Db.Scans.SingleAsync(); await s.Handler.ApplyAsync(scan, default);
        Assert.Equal("Excepcion", scan.Resultado); Assert.Equal("PT_PEDIDO_INEXISTENTE", scan.Motivo);
        s.Source.Order = source with { State = "Cancelado" };
        Assert.Equal("PT_PEDIDO_CANCELADO", (await s.Handler.Handle(s.Input() with { SourceEventId = "cancelled" }, default)).Reason);
    }
    [Theory][InlineData("E48", 3, 0)][InlineData("H87", 3, 3)][InlineData("H87", 1.5, 0)]
    public void Services_and_fractional_quantities_do_not_generate_pieces(string unit, double quantity, int expected)
        => Assert.Equal(expected, new PtOrderLineSource(1, null, "Glass", (decimal)quantity, unit).ExpectedPieces);
    [Fact]
    public async Task Discard_requires_reason_and_records_actor_and_time()
    {
        using var s = new Scenario(); await s.Handler.Handle(s.Input() with { BranchId = null }, default);
        var scan = await s.Db.Scans.SingleAsync(); var user = Guid.NewGuid();
        Assert.Throws<BusinessRuleException>(() => scan.Resolve(user, " ", s.Clock.Now, true));
        scan.Resolve(user, "Etiqueta incorrecta", s.Clock.Now, true);
        Assert.Equal("Descartado", scan.Resultado); Assert.Equal(user, scan.ResueltoPor); Assert.Equal(s.Clock.Now, scan.ResueltoAt); Assert.Equal("Etiqueta incorrecta", scan.ResolutionReason);
    }
    [Fact]
    public async Task Peak_2500_scans_with_10_percent_repeats_has_no_losses()
    {
        using var s = new Scenario(2250); var timer = Stopwatch.StartNew();
        for (var i = 0; i < 2500; i++)
        {
            var number = i % 2250 + 1;
            await s.Handler.Handle(s.Input() with { PieceNumber = number, SourceEventId = "load-" + number }, default);
            s.Db.ChangeTracker.Clear();
        }
        Assert.Equal(2250, await s.Db.Pieces.CountAsync()); Assert.Equal(2250, await s.Db.Movements.CountAsync()); Assert.Equal(2250, await s.Db.OutboxEntries.CountAsync());
        output.WriteLine($"2,500 escaneos, 2,250 piezas, 0 pérdidas: {timer.Elapsed.TotalSeconds:F3} s (EF InMemory, sin red).");
    }
    [Fact]
    public void Initial_migration_matches_model_and_has_no_cross_schema_foreign_keys()
    {
        using var db = new ProductoTerminadoDbContext(new DbContextOptionsBuilder<ProductoTerminadoDbContext>()
            .UseNpgsql("Host=localhost;Database=millet_design_only").UseSnakeCaseNamingConvention().Options, new CompanyContext());
        Assert.False(db.Database.HasPendingModelChanges());
        var entities = db.Model.GetEntityTypes().Where(t => t.GetSchema() == ProductoTerminadoDbContext.SchemaName).ToArray();
        Assert.Equal(7, entities.Length);
        Assert.All(entities.SelectMany(t => t.GetForeignKeys()), fk => Assert.Equal(ProductoTerminadoDbContext.SchemaName, fk.PrincipalEntityType.GetSchema()));
    }
    private sealed class Scenario : IDisposable
    {
        public Guid Company { get; } = Guid.NewGuid(); public Guid Branch { get; } = Guid.NewGuid();
        public Source Source { get; } = new(); public Clock Clock { get; } = new();
        public ProductoTerminadoDbContext Db { get; } public PtScanHandler Handler { get; }
        public Scenario(int quantity = 3)
        {
            Db = new(new DbContextOptionsBuilder<ProductoTerminadoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new CompanyContext());
            Source.Order = new(Guid.NewGuid(), "900101", Branch, "Importado", "CONTADO", Guid.NewGuid(), "MXN", 100, 1, [new(1, Guid.NewGuid(), "Glass", quantity, "H87")]);
            Handler = new(Db, Source, Clock, Options.Create(new PtScanOptions()));
        }
        public RegistrarEscaneoPieza Input() => new(Company, "Demo", "event", "900101", 1, 1, "CIRCUITO", Branch, Clock.Now);
        public void Dispose() => Db.Dispose();
    }
    private sealed class Source : IPedidosPtReadPort
    { public PtOrderSource? Order; public Task<PtOrderSource?> GetAsync(Guid company, string number, CancellationToken ct) => Task.FromResult(Order); }
    private sealed class Clock : IClock { public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-30T12:00:00Z"); public DateTimeOffset UtcNow => Now; }
    private sealed class CompanyContext : ICurrentEmpresaContext
    { public Guid? Current => null; public bool IsBypassed => true; public IDisposable Bypass() => throw new NotSupportedException(); }
}
