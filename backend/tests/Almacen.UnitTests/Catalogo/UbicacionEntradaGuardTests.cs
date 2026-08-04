using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Asignaciones;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.Saldos;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Catalogo;

/// <summary>
/// Tests C7.2b (InMemory) de la lógica nueva de captura de bin: el guard de
/// entrada compartido (<see cref="UbicacionEntradaGuard"/>), el guardrail
/// <c>ASIGNACION_A_UNICA</c> del handler de asignar, y la query
/// saldo-por-ubicación que puebla el selector de salida.
/// </summary>
public class UbicacionEntradaGuardTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();

    private static AlmacenDbContext NuevaDb()
    {
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-c72b-{Guid.NewGuid():N}")
            .Options;
        var db = new AlmacenDbContext(opts, new FakeEmpresa(EmpresaId));
        db.Database.EnsureCreated();
        return db;
    }

    private static (AlmacenDbContext Db, Guid SubId, Guid RackId, Guid UnicaId, Guid ArticuloId) DbConRack()
    {
        var db = NuevaDb();
        var almacenId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        var unicaId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        // Salida-por-línea C1: SaldosPorUbicacion ahora hace join a SubAlmacén (N3)
        // y Almacén (N2) para la ruta del selector. En prod las FK garantizan los
        // padres; el fixture InMemory debe sembrarlos o el INNER join descarta
        // las filas (los guards de entrada no los necesitaban antes).
        db.Almacenes.Add(
            new Millet.Almacen.Domain.Catalogo.Almacen(almacenId, "ALM", "Almacén test", Guid.NewGuid()));
        db.SubAlmacenes.Add(
            new SubAlmacen(subId, almacenId, "SUB", "Sub test", TipoSubAlmacen.Insumos));
        db.Ubicaciones.Add(new Ubicacion(unicaId, subId, "ÚNICA", "Única", esDefault: true));
        db.Ubicaciones.Add(new Ubicacion(rackId, subId, "R-1", "Rack 1", esDefault: false));
        db.SaveChanges();
        return (db, subId, rackId, unicaId, articuloId);
    }

    // ─── Guard de entrada ────────────────────────────────────────────────────

    [Fact]
    public async Task Guard_pasa_con_bin_real_asignado()
    {
        var (db, subId, rackId, _, articuloId) = DbConRack();
        await using var _ = db;
        db.AsignacionesArticuloUbicacion.Add(
            new AsignacionArticuloUbicacion(Guid.NewGuid(), rackId, articuloId));
        await db.SaveChangesAsync();

        var act = () => UbicacionEntradaGuard.ValidarUbicacionEntradaAsync(
            db, rackId, subId, articuloId, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Guard_rechaza_bin_de_otro_sub_almacen()
    {
        var (db, _, rackId, _, articuloId) = DbConRack();
        await using var _ = db;
        db.AsignacionesArticuloUbicacion.Add(
            new AsignacionArticuloUbicacion(Guid.NewGuid(), rackId, articuloId));
        await db.SaveChangesAsync();

        var act = () => UbicacionEntradaGuard.ValidarUbicacionEntradaAsync(
            db, rackId, Guid.NewGuid(), articuloId, CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("UBICACION_NO_PERTENECE_AL_SUBALMACEN");
    }

    [Fact]
    public async Task Guard_rechaza_entrada_a_la_unica()
    {
        var (db, subId, _, unicaId, articuloId) = DbConRack();
        await using var _ = db;

        var act = () => UbicacionEntradaGuard.ValidarUbicacionEntradaAsync(
            db, unicaId, subId, articuloId, CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("ENTRADA_A_UBICACION_UNICA");
    }

    [Fact]
    public async Task Guard_rechaza_bin_sin_asignacion()
    {
        var (db, subId, rackId, _, articuloId) = DbConRack();
        await using var _ = db;
        // Rack real y del sub-almacén, pero el artículo NO está asignado.

        var act = () => UbicacionEntradaGuard.ValidarUbicacionEntradaAsync(
            db, rackId, subId, articuloId, CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("ENTRADA_SIN_ASIGNACION");
    }

    // ─── Camino recepción: sin sub de cabecera, DERIVA el sub del bin ────────

    [Fact]
    public async Task Guard_recepcion_sin_sub_deriva_el_sub_del_bin()
    {
        // Camino recepción: SIN sub de cabecera. Valida y RETORNA el sub del bin.
        var (db, subId, rackId, _, articuloId) = DbConRack();
        await using var _ = db;
        db.AsignacionesArticuloUbicacion.Add(
            new AsignacionArticuloUbicacion(Guid.NewGuid(), rackId, articuloId));
        await db.SaveChangesAsync();

        var sub = await UbicacionEntradaGuard.ValidarEntradaYDerivarSubAsync(
            db, rackId, articuloId, CancellationToken.None);
        sub.Should().Be(subId);
    }

    [Fact]
    public async Task Guard_recepcion_sin_sub_igual_rechaza_la_unica()
    {
        // Sin sub de cabecera, las reglas de ÚNICA (3) y asignación (4) siguen
        // aplicando; solo desaparece la comparación de pertenencia (regla 2).
        var (db, _, _, unicaId, articuloId) = DbConRack();
        await using var _ = db;

        var act = () => UbicacionEntradaGuard.ValidarEntradaYDerivarSubAsync(
            db, unicaId, articuloId, CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("ENTRADA_A_UBICACION_UNICA");
    }

    // ─── ASIGNACION_A_UNICA (handler de asignar) ─────────────────────────────

    [Fact]
    public async Task Asignar_a_la_unica_es_rechazado()
    {
        var (db, _, _, unicaId, articuloId) = DbConRack();
        await using var _ = db;
        var handler = new AsignarArticuloAUbicacionHandler(db, new FakeArticulos());

        var act = () => handler.Handle(
            new AsignarArticuloAUbicacionCommand(unicaId, articuloId), CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("ASIGNACION_A_UNICA");
    }

    // ─── Saldo por ubicación (selector de salida) ────────────────────────────

    [Fact]
    public async Task SaldosPorUbicacion_incluye_unica_con_stock_y_excluye_ceros()
    {
        var (db, subId, rackId, unicaId, articuloId) = DbConRack();
        await using var _ = db;
        var rackVacioId = Guid.NewGuid();
        db.Ubicaciones.Add(new Ubicacion(rackVacioId, subId, "R-2", "Rack 2 vacío", esDefault: false));
        // ÚNICA con 8, rack con 5, rack vacío con 0.
        SembrarSaldo(db, unicaId, subId, articuloId, 8m, 100m);
        SembrarSaldo(db, rackId, subId, articuloId, 5m, 120m);
        SembrarSaldo(db, rackVacioId, subId, articuloId, 0m, 0m);
        await db.SaveChangesAsync();

        var res = await new SaldosPorUbicacionHandler(db).Handle(
            new SaldosPorUbicacionQuery(articuloId, subId), CancellationToken.None);

        res.Should().HaveCount(2); // el rack vacío (0) no aparece
        res.Should().Contain(x => x.UbicacionId == unicaId && x.EsDefault && x.Cantidad == 8m);
        res.Should().Contain(x => x.UbicacionId == rackId && !x.EsDefault && x.Cantidad == 5m);
        res.Should().NotContain(x => x.UbicacionId == rackVacioId);
    }

    private static void SembrarSaldo(
        AlmacenDbContext db, Guid ubicacionId, Guid subId, Guid articuloId,
        decimal cantidad, decimal costo)
    {
        db.SaldosInventario.Add(
            new SaldoInventario(ubicacionId, subId, articuloId, cantidad, costo));
    }

    // ─── Fakes ───────────────────────────────────────────────────────────────

    private sealed class FakeEmpresa(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeArticulos : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken ct) =>
            Task.FromResult<ArticuloLectura?>(
                new ArticuloLectura(articuloId, "ART", "Artículo", "PZA", null, null, true));
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(
                new Dictionary<Guid, ArticuloLectura>());
    }
}
