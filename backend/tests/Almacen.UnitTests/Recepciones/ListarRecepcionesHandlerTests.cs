using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>
/// Tests de la bandeja de recepciones (ADR-0042). Verifica que el folio de OC
/// se resuelve en <b>batch</b> sobre los OcId distintos de la página (no uno
/// por fila), que dos recepciones de la misma OC comparten una sola entrada en
/// la consulta de folios, y que las filas sin OC (o cuya OC no resuelve) caen a
/// <c>OrdenCompraFolio = null</c> (el frontend cae al id truncado).
/// </summary>
public class ListarRecepcionesHandlerTests
{
    private static readonly Guid Oc1 = Guid.NewGuid();
    private static readonly Guid Oc2 = Guid.NewGuid();

    [Fact]
    public async Task Busca_por_pedimento_normalizado_y_respeta_documentos_permitidos()
    {
        await using var db = await NuevaDbAsync();
        AgregarRecepcion(db, Oc1, "DEMO", new(2026, 10, 22), "M-ENT2026-000001");
        var mov = db.Movimientos.Local.Single();
        var importacion = new RecepcionImportacion(mov.EmpresaId, Oc1, "261612346000001", "BL-DEMO", "CONT-DEMO", "16", "A1", []);
        importacion.VincularRecepcion(mov.Id);
        db.RecepcionesImportacion.Add(importacion);
        await db.SaveChangesAsync();
        var handler = new ListarRecepcionesHandler(db, new FakeOcReadPort());
        var consulta = new ListarRecepcionesQuery(null, null, null, null, null, 0, 50, Q: "26 16 1234 6000001");
        Assert.Single((await handler.Handle(consulta, default)).Items);
        Assert.Single((await handler.Handle(consulta with { Q = "6000001" }, default)).Items);
        Assert.Empty((await handler.Handle(consulta with { Q = "9999" }, default)).Items);
        Assert.Empty((await handler.Handle(consulta with { DocumentosPermitidos = [] }, default)).Items);
    }

    [Fact]
    public async Task Resuelve_folios_de_OC_en_batch_y_cae_a_null_sin_OC()
    {
        await using var db = await NuevaDbAsync();
        // 2 recepciones de Oc1 (consolidación → un solo id distinto), 1 de Oc2,
        // 1 sin OC.
        AgregarRecepcion(db, Oc1, "ACC-001", new DateOnly(2026, 6, 4));
        AgregarRecepcion(db, Oc1, "ACC-002", new DateOnly(2026, 6, 3));
        AgregarRecepcion(db, Oc2, "ACC-003", new DateOnly(2026, 6, 2));
        AgregarRecepcion(db, ocId: null, "ACC-004", new DateOnly(2026, 6, 1));
        await db.SaveChangesAsync();

        var ocs = new FakeOcReadPort
        {
            Folios = new() { [Oc1] = "OC-MID2026-000010", [Oc2] = "OC-MID2026-000020" },
        };
        var handler = new ListarRecepcionesHandler(db, ocs);

        var page = await handler.Handle(
            new ListarRecepcionesQuery(null, null, null, null, null, Offset: 0, Limit: 50),
            CancellationToken.None);

        page.Items.Should().HaveCount(4);
        page.Items.Where(i => i.OrdenCompraId == Oc1)
            .Should().OnlyContain(i => i.OrdenCompraFolio == "OC-MID2026-000010");
        page.Items.Single(i => i.OrdenCompraId == Oc2).OrdenCompraFolio.Should().Be("OC-MID2026-000020");
        page.Items.Single(i => i.OrdenCompraId == null).OrdenCompraFolio.Should().BeNull();

        // Batch real: UNA sola llamada con los OcId DISTINTOS de la página
        // (Oc1 una vez pese a tener 2 recepciones; la fila sin OC no entra).
        ocs.Llamadas.Should().Be(1);
        ocs.UltimosIds.Should().BeEquivalentTo(new[] { Oc1, Oc2 });
    }

    [Fact]
    public async Task Cae_a_null_cuando_la_OC_no_resuelve()
    {
        await using var db = await NuevaDbAsync();
        AgregarRecepcion(db, Oc1, "ACC-001", new DateOnly(2026, 6, 4));
        await db.SaveChangesAsync();

        var handler = new ListarRecepcionesHandler(db, new FakeOcReadPort()); // diccionario vacío

        var page = await handler.Handle(
            new ListarRecepcionesQuery(null, null, null, null, null, Offset: 0, Limit: 50),
            CancellationToken.None);

        var item = page.Items.Single();
        item.OrdenCompraId.Should().Be(Oc1);
        item.OrdenCompraFolio.Should().BeNull();
    }

    [Fact]
    public async Task Filtra_por_proveedor_via_OC_y_busca_por_folio()
    {
        await using var db = await NuevaDbAsync();
        AgregarRecepcion(db, Oc1, "A", new DateOnly(2026, 6, 4), "M-ENT2026-000001");
        AgregarRecepcion(db, Oc2, "B", new DateOnly(2026, 6, 3), "M-ENT2026-000002");
        await db.SaveChangesAsync();
        var proveedor = Guid.NewGuid();
        var handler = new ListarRecepcionesHandler(db, new FakeOcReadPort { OcsPorProveedor = new() { [proveedor] = new[] { Oc2 } } });

        var porProveedor = await handler.Handle(
            new ListarRecepcionesQuery(null, null, null, null, null, 0, 50, ProveedorId: proveedor),
            CancellationToken.None);
        porProveedor.Items.Should().ContainSingle().Which.OrdenCompraId.Should().Be(Oc2);

        var porFolio = await handler.Handle(
            new ListarRecepcionesQuery(null, null, null, null, null, 0, 50, Q: "000001"),
            CancellationToken.None);
        porFolio.Items.Should().ContainSingle().Which.OrdenCompraId.Should().Be(Oc1);
    }

    [Fact]
    public async Task Oculta_el_movimiento_de_revision_hermano_de_la_recepcion_principal()
    {
        await using var db = await NuevaDbAsync();
        AgregarRecepcion(db, Oc1, "A", new DateOnly(2026, 6, 4), "M-ENT2026-000001");
        AgregarRecepcion(db, Oc1, "A", new DateOnly(2026, 6, 4), "M-ENT2026-000002");
        await db.SaveChangesAsync();
        var principal = db.Movimientos.Local.Single(m => m.Folio == "M-ENT2026-000001");
        var revision = db.Movimientos.Local.Single(m => m.Folio == "M-ENT2026-000002");
        typeof(MovimientoInventario).GetProperty("RecepcionOrigenId")!.SetValue(revision, principal.Id);
        await db.SaveChangesAsync();

        var page = await new ListarRecepcionesHandler(db, new FakeOcReadPort()).Handle(
            new ListarRecepcionesQuery(null, null, null, null, null, Offset: 0, Limit: 50), CancellationToken.None);

        page.Items.Should().ContainSingle().Which.Id.Should().Be(principal.Id);
    }

    // ─── Infra de test ───

    private static readonly Guid UbicId = Guid.NewGuid();
    private static readonly Guid SubId = Guid.NewGuid();

    private static void AgregarRecepcion(
        AlmacenDbContext db, Guid? ocId, string articuloClave, DateOnly fecha, string? folio = null)
    {
        var mov = new MovimientoInventario(
            id: Guid.NewGuid(),
            tipo: TipoMovimiento.EntradaCompra,
            empresaId: Guid.NewGuid(),
            fechaMovimiento: fecha);

        // PR6a: la línea trae bin; el sub se deriva de él vía la vista.
        mov.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), mov.Id, 1, Guid.NewGuid(), 1m, "PZA", 10m, ubicacionId: UbicId));

        if (ocId is Guid oc)
        {
            typeof(MovimientoInventario)
                .GetMethod("VincularRecepcionVarianteA", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(mov, new object?[]
                {
                    // El vínculo fiscal es obligatorio en variante A; la
                    // bandeja solo necesita la OC, así que va por UUID.
                    oc, null, null, "AD662D33-6934-459C-A128-BDF0393E0062",
                });
        }

        if (folio is not null) mov.Registrar(new FolioMovimiento(folio), Guid.NewGuid());
        db.Movimientos.Add(mov);
    }

    private static async Task<AlmacenDbContext> NuevaDbAsync()
    {
        var opts = new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-recepcion-bandeja-{Guid.NewGuid():N}")
            .Options;
        var db = new InMemoryAlmacenDbContext(opts, new BypassedEmpresaContext());
        await db.Database.EnsureCreatedAsync();
        db.Ubicaciones.Add(new Ubicacion(UbicId, SubId, "U1", "Ubicación 1"));
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class FakeOcReadPort : IComprasOcReadPort
    {
        public Dictionary<Guid, string> Folios { get; set; } = new();
        public Dictionary<Guid, Guid[]> OcsPorProveedor { get; set; } = new();
        public int Llamadas { get; private set; }

        public Task<IReadOnlyCollection<Guid>> ObtenerIdsPorProveedorAsync(
            Guid proveedorId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Guid>>(
                OcsPorProveedor.GetValueOrDefault(proveedorId) ?? Array.Empty<Guid>());
        public Guid[] UltimosIds { get; private set; } = Array.Empty<Guid>();

        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken cancellationToken) =>
            Task.FromResult<OcLectura?>(null);

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
            IReadOnlyCollection<Guid> ocIds, CancellationToken cancellationToken)
        {
            Llamadas++;
            UltimosIds = ocIds.ToArray();
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                Folios.Where(kv => ocIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
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
