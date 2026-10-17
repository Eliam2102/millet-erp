using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Application.Vales;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.UnitTests.Salidas;

/// <summary>ALM-04: el material en revisión (MAT-REV) no se surte con RQ ni con vale hasta el dictamen.</summary>
public class SalidaDesdeRevisionTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid AlmacenId = Guid.NewGuid();
    private static readonly Guid SubRevId = Guid.NewGuid();
    private static readonly Guid RevBinId = Guid.NewGuid();
    private static readonly Guid ArticuloId = Guid.NewGuid();

    private static async Task<InMemoryAlmacenDbContext> NuevaDbAsync()
    {
        var opts = new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-salida-rev-{Guid.NewGuid():N}").Options;
        var db = new InMemoryAlmacenDbContext(opts, new FakeEmpresa());
        await db.Database.EnsureCreatedAsync();
        db.Almacenes.Add(new Almacen.Domain.Catalogo.Almacen(AlmacenId, "ALM", "Almacén", Guid.NewGuid()));
        db.SubAlmacenes.Add(new SubAlmacen(SubRevId, AlmacenId, "MAT-REV", "Material en revisión", TipoSubAlmacen.MaterialEnRevision));
        db.Ubicaciones.Add(new Ubicacion(RevBinId, SubRevId, "REV-01", "Revisión 1"));
        db.SaldosInventario.Add(new SaldoInventario(RevBinId, SubRevId, ArticuloId, 2, 0));
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Salida_con_requisicion_desde_bin_de_revision_falla()
    {
        await using var db = await NuevaDbAsync();
        var h = new RegistrarSalidaConRequisicionHandler(db, new FakeRq(), new CapturaEventos(),
            new FakeUser(), new FakeEmpresa(), new FakeDecimales(), new PeriodoContableStub(true), new P7Support.Conversion(), P7Support.Apartados(db));

        var act = () => h.Handle(new RegistrarSalidaConRequisicionCommand(Guid.NewGuid(), new DateOnly(2026, 10, 17),
            null, null,
            [new RegistrarSalidaLineaInput(ArticuloId, ArticuloId, 1m, null, null, null, null, RevBinId)]), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("SALIDA_DESDE_REVISION");
    }

    [Fact]
    public async Task Salida_por_vale_desde_bin_de_revision_falla()
    {
        await using var db = await NuevaDbAsync();
        var h = new RegistrarSalidaPorValeHandler(db, new CapturaEventos(), new FakeUser(), new FakeEmpresa(), new FakeDecimales(),
            new PeriodoContableStub(true), new P1Fixture.Calendario(), new P1Fixture.Centros(), new P7Support.Conversion(), P7Support.Apartados(db));

        var act = () => h.Handle(new RegistrarSalidaPorValeCommand(new DateOnly(2026, 10, 17), "blob://vale", null, null,
            [new RegistrarSalidaLineaInput(ArticuloId, ArticuloId, 1m, null, null, null, null, RevBinId)]), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("SALIDA_DESDE_REVISION");
    }

    private sealed class CapturaEventos : IIntegrationEventPublisher
    {
        public Task PublishAsync(object integrationEvent, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeRq : IComprasRequisicionReadPort
    {
        public Task<RequisicionLectura?> ObtenerAsync(Guid rqId, CancellationToken ct) =>
            Task.FromResult<RequisicionLectura?>(new(rqId, "RQ-P1", EmpresaId, EmpresaId, AlmacenId, null, "EnSurtido", [new(ArticuloId, ArticuloId, "PZA", 100, 0, null, null, 100)]));
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
            IReadOnlyCollection<Guid> rqIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class FakeUser : ICurrentUserContext
    {
        public Guid? UserId => EmpresaId;
        public string? UserName => "test";
    }

    private sealed class FakeDecimales : IDecimalesUnidadGuard
    {
        public Task ValidarAsync(IEnumerable<CantidadAValidar> cantidades, CancellationToken ct) => Task.CompletedTask;
    }
}
