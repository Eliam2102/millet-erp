using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Saldos;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Saldos;

/// <summary>M-231 (InMemory): traslado de hojas entre posiciones; el saldo lo mueve el trigger de PG (se ve en integración).</summary>
public class TrasladoPosicionTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly Guid Articulo = Guid.NewGuid();
    private static readonly Guid Variante = Guid.NewGuid();
    private const decimal AreaHoja = 2.44m * 3.66m;

    private static (AlmacenDbContext Db, SaldoInventario Saldo, Guid RackB, Guid RackC) Db()
    {
        var db = new AlmacenDbContext(new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"traslado-{Guid.NewGuid():N}").Options, new FakeEmpresa(EmpresaId));
        db.Database.EnsureCreated();
        var alm = Guid.NewGuid(); var sub = Guid.NewGuid();
        var rackA = Guid.NewGuid(); var rackB = Guid.NewGuid(); var rackC = Guid.NewGuid();
        db.Almacenes.Add(new Domain.Catalogo.Almacen(alm, "MP-CNK", "Materia prima Conkal", Guid.NewGuid()));
        db.SubAlmacenes.Add(new SubAlmacen(sub, alm, "VID", "Vidrio", TipoSubAlmacen.MaterialesDirectos));
        db.Ubicaciones.Add(new Ubicacion(rackA, sub, "R-A", "Rack A"));
        db.Ubicaciones.Add(new Ubicacion(rackB, sub, "R-B", "Rack B"));
        db.Ubicaciones.Add(new Ubicacion(rackC, sub, "R-C", "Rack C"));
        db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), rackA, Articulo));
        db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), rackB, Articulo));
        var saldo = new SaldoInventario(rackA, sub, Articulo, 10 * AreaHoja, 250m, Variante, "L-2611");
        db.SaldosInventario.Add(saldo);
        db.SaveChanges();
        return (db, saldo, rackB, rackC);
    }

    private static Task<TrasladoPosicionResultado> Trasladar(AlmacenDbContext db, TrasladarPosicion c) =>
        TrasladoPosicionApp.RegistrarAsync(db, new Medidas(), new Articulos(), new PeriodoContableStub(true), EmpresaId, Usuario, c, default);

    [Fact]
    public async Task Mover_3_hojas_del_rack_A_al_B_es_un_traslado_con_un_folio_mismo_lote_y_costo()
    {
        var (db, saldo, rackB, _) = Db();

        var r = await Trasladar(db, new(saldo.Id, TrasladoPosicionApp.Version(saldo.UltimaActualizacionAt), 3, rackB, "Reacomodo de rack"));
        await db.SaveChangesAsync();

        r.Folio.Should().Contain("TRA");
        var movs = await db.Movimientos.Include(m => m.Lineas).ToListAsync();
        movs.Should().HaveCount(2);
        var salida = movs.Single(m => m.Tipo == TipoMovimiento.TrasladoInterno);
        var entrada = movs.Single(m => m.Tipo == TipoMovimiento.EntradaTrasladoInterno);
        salida.Id.Should().Be(r.MovimientoId);
        salida.Lineas.Should().ContainSingle(l => l.Cantidad == Math.Round(3 * AreaHoja, 4) && l.UbicacionId == saldo.UbicacionId && l.Lote == "L-2611" && l.CostoUnitarioMxn == 250m);
        entrada.Lineas.Should().ContainSingle(l => l.UbicacionId == rackB && l.Lote == "L-2611" && l.CostoUnitarioMxn == 250m);
        salida.Motivo.Should().Contain("R-A → R-B");
    }

    [Fact]
    public async Task Segunda_pantalla_con_version_vieja_recibe_conflicto_y_no_mueve()
    {
        var (db, saldo, rackB, _) = Db();

        var act = () => Trasladar(db, new(saldo.Id, "0", 3, rackB, null));

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("TRASLADO_SALDO_CAMBIO");
        db.ChangeTracker.Entries<MovimientoInventario>().Should().BeEmpty();
    }

    [Theory]
    [InlineData(2.5, "HOJAS_ENTERAS")]
    [InlineData(11, "TRASLADO_SALDO_INSUFICIENTE")]
    public async Task Hojas_enteras_y_sin_pasar_lo_disponible(decimal hojas, string codigo)
    {
        var (db, saldo, rackB, _) = Db();
        var act = () => Trasladar(db, new(saldo.Id, TrasladoPosicionApp.Version(saldo.UltimaActualizacionAt), hojas, rackB, null));
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(codigo);
    }

    [Fact]
    public async Task Posicion_destino_sin_asignacion_del_articulo_no_se_acepta()
    {
        var (db, saldo, _, rackC) = Db();
        var act = () => Trasladar(db, new(saldo.Id, TrasladoPosicionApp.Version(saldo.UltimaActualizacionAt), 1, rackC, null));
        await act.Should().ThrowAsync<Millet.SharedKernel.Domain.Exceptions.DomainException>();
    }

    private sealed class Medidas : IVarianteVidrioReadPort
    {
        public Task<VarianteVidrioLectura> ObtenerAsync(Guid articuloId, Guid varianteId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, VarianteVidrioLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, VarianteVidrioLectura>>(ids.Where(i => i == Variante)
                .ToDictionary(i => i, i => new VarianteVidrioLectura(i, "FL6-2440x3660", 2440m, 3660m, 6m)));
        public Task<IReadOnlyList<VarianteVidrioLectura>> ListarPorArticuloAsync(Guid articuloId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Articulos : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken ct) => Task.FromResult<ArticuloLectura?>(null);
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(new Dictionary<Guid, ArticuloLectura>());
    }

    private sealed class FakeEmpresa(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
