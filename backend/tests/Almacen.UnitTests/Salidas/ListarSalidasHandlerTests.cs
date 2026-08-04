using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.Salidas;

/// <summary>
/// Tests de la bandeja de salidas (ADR-0042): la columna RQ muestra folio,
/// no GUID. El folio se resuelve en <b>batch</b> sobre los RqId distintos de
/// la página vía <see cref="IComprasRequisicionReadPort"/> (mismo puerto
/// state-agnostic que usa el detalle); las salidas sin RQ (vales) y las RQs
/// que el puerto no resuelve caen a <c>RqFolio = null</c> (el frontend
/// muestra el id truncado como fallback).
/// </summary>
public class ListarSalidasHandlerTests
{
    private static readonly Guid Rq1 = Guid.NewGuid();
    private static readonly Guid Rq2 = Guid.NewGuid();
    private static readonly Guid RqReg = Guid.NewGuid();

    [Fact]
    public async Task Resuelve_folios_de_RQ_directa_y_regularizadora_en_un_solo_batch()
    {
        await using var db = await NuevaDbAsync();
        // 2 salidas de Rq1 (batch → un solo id distinto), 1 de Rq2, 1 vale
        // sin regularizar y 1 vale regularizado con RqReg.
        AgregarSalida(db, Rq1, new DateOnly(2026, 7, 5));
        AgregarSalida(db, Rq1, new DateOnly(2026, 7, 4));
        AgregarSalida(db, Rq2, new DateOnly(2026, 7, 3));
        AgregarSalida(db, rqId: null, new DateOnly(2026, 7, 2));
        AgregarSalida(db, rqId: null, new DateOnly(2026, 7, 1), rqRegularizadoraId: RqReg);
        await db.SaveChangesAsync();

        var rqs = new FakeRequisicionReadPort
        {
            Folios = new()
            {
                [Rq1] = "RQ-MID2026-000010",
                [Rq2] = "RQ-MID2026-000020",
                [RqReg] = "RQ-MID2026-000099",
            },
        };
        var handler = new ListarSalidasHandler(db, rqs);

        var page = await handler.Handle(
            new ListarSalidasQuery(null, null, null, null, null, null, null, null, Offset: 0, Limit: 50),
            CancellationToken.None);

        page.Items.Should().HaveCount(5);
        // Salidas por RQ directa → RqFolio.
        page.Items.Where(i => i.RqId == Rq1)
            .Should().OnlyContain(i => i.RqFolio == "RQ-MID2026-000010" && i.RqRegularizadoraFolio == null);
        page.Items.Single(i => i.RqId == Rq2).RqFolio.Should().Be("RQ-MID2026-000020");
        // Vale sin regularizar → ambos null (el FE muestra "—").
        var valeSinReg = page.Items.Single(i => i.RqId == null && i.RqRegularizadoraId == null);
        valeSinReg.RqFolio.Should().BeNull();
        valeSinReg.RqRegularizadoraFolio.Should().BeNull();
        // Vale regularizado → folio de la RQ regularizadora (el FE lo
        // etiqueta "Vale · {folio}").
        var valeReg = page.Items.Single(i => i.RqRegularizadoraId == RqReg);
        valeReg.RqFolio.Should().BeNull();
        valeReg.RqRegularizadoraFolio.Should().Be("RQ-MID2026-000099");

        // Batch real: UNA llamada con los ids DISTINTOS de ambas fuentes
        // (Rq1 una vez pese a 2 salidas; el vale sin RQ no aporta).
        rqs.Llamadas.Should().Be(1);
        rqs.UltimosIds.Should().BeEquivalentTo(new[] { Rq1, Rq2, RqReg });
    }

    [Fact]
    public async Task Cae_a_null_cuando_la_RQ_no_resuelve()
    {
        await using var db = await NuevaDbAsync();
        AgregarSalida(db, Rq1, new DateOnly(2026, 7, 4));
        await db.SaveChangesAsync();

        var handler = new ListarSalidasHandler(db, new FakeRequisicionReadPort()); // dict vacío

        var page = await handler.Handle(
            new ListarSalidasQuery(null, null, null, null, null, null, null, null, Offset: 0, Limit: 50),
            CancellationToken.None);

        var item = page.Items.Single();
        item.RqId.Should().Be(Rq1);
        item.RqFolio.Should().BeNull();
    }

    [Fact]
    public async Task Pagina_sin_RQs_no_llama_al_puerto()
    {
        await using var db = await NuevaDbAsync();
        AgregarSalida(db, rqId: null, new DateOnly(2026, 7, 1)); // solo un vale
        await db.SaveChangesAsync();

        var rqs = new FakeRequisicionReadPort();
        var handler = new ListarSalidasHandler(db, rqs);

        await handler.Handle(
            new ListarSalidasQuery(null, null, null, null, null, null, null, null, Offset: 0, Limit: 50),
            CancellationToken.None);

        rqs.Llamadas.Should().Be(0);
    }

    // ─── Infra de test ───

    private static readonly Guid UbicId = Guid.NewGuid();
    private static readonly Guid SubId = Guid.NewGuid();

    private static void AgregarSalida(
        AlmacenDbContext db, Guid? rqId, DateOnly fecha, Guid? rqRegularizadoraId = null)
    {
        var esVale = rqId is null;
        var mov = new MovimientoInventario(
            id: Guid.NewGuid(),
            tipo: esVale ? TipoMovimiento.SalidaPorVale : TipoMovimiento.SalidaConsumo,
            empresaId: Guid.NewGuid(),
            fechaMovimiento: fecha);

        // PR6a: la línea trae bin; el sub se deriva de él (esta bandeja no
        // asserta el sub, pero el handler consulta la vista).
        mov.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), mov.Id, 1, Guid.NewGuid(), 1m, "PZA", 10m, ubicacionId: UbicId));

        // VincularSalida fija RqId (salida por RQ) o el estado pendiente-de-
        // regularizar (vale, A14) — requisito para poder RegularizarVale.
        typeof(MovimientoInventario)
            .GetMethod("VincularSalida", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(mov, new object?[] { rqId, esVale ? "blob://vale.pdf" : null, null });

        if (rqRegularizadoraId is Guid regId) mov.RegularizarVale(regId);

        db.Movimientos.Add(mov);
    }

    private static async Task<AlmacenDbContext> NuevaDbAsync()
    {
        var opts = new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-salidas-bandeja-{Guid.NewGuid():N}")
            .Options;
        var db = new InMemoryAlmacenDbContext(opts, new BypassedEmpresaContext());
        await db.Database.EnsureCreatedAsync();
        db.Ubicaciones.Add(new Ubicacion(UbicId, SubId, "U1", "Ubicación 1"));
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class FakeRequisicionReadPort : IComprasRequisicionReadPort
    {
        public Dictionary<Guid, string> Folios { get; set; } = new();
        public int Llamadas { get; private set; }
        public Guid[] UltimosIds { get; private set; } = Array.Empty<Guid>();

        public Task<RequisicionLectura?> ObtenerAsync(Guid rqId, CancellationToken cancellationToken) =>
            Task.FromResult<RequisicionLectura?>(null);

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
            IReadOnlyCollection<Guid> rqIds, CancellationToken cancellationToken)
        {
            Llamadas++;
            UltimosIds = rqIds.ToArray();
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                Folios.Where(kv => rqIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
        }
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
