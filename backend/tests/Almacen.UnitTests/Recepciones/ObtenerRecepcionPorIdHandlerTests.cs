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
/// Tests del enriquecimiento de nombres en el detalle de recepción (ADR-0042,
/// 3er aplicador). Verifica que los 3 campos (sub-almacén, folio de OC,
/// artículo por línea) resuelven a nombre, que caen al id cuando el read-port
/// no resuelve, y que el artículo se resuelve en batch (sin N+1).
/// </summary>
public class ObtenerRecepcionPorIdHandlerTests
{
    private static readonly Guid SubId = Guid.NewGuid();
    private static readonly Guid ArtA = Guid.NewGuid();
    private static readonly Guid ArtB = Guid.NewGuid();
    private static readonly Guid OcId = Guid.NewGuid();
    private static readonly Guid CfdiId = Guid.NewGuid();
    private static readonly Guid FactId = Guid.NewGuid();

    [Fact]
    public async Task Resuelve_los_tres_campos_a_nombre()
    {
        await using var db = await NuevaDbConRecepcionAsync(conSubAlmacen: true);
        var articulos = new FakeArticuloReadPort
        {
            PorIds = new Dictionary<Guid, ArticuloLectura>
            {
                [ArtA] = Art(ArtA, "ACC-001", "Tornillo M6"),
                [ArtB] = Art(ArtB, "ACC-002", "Tuerca M6"),
            },
        };
        var ocs = new FakeOcReadPort { Folios = new() { [OcId] = "OC-MID2026-000050" } };

        var handler = new ObtenerRecepcionPorIdHandler(db, articulos, ocs, new FakeCxpDocumentosReadPort());
        var dto = await handler.Handle(new ObtenerRecepcionPorIdQuery(RecepcionId(db)), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.SubAlmacenClave.Should().Be("ALM-CEN");
        dto.SubAlmacenNombre.Should().Be("Almacén Central");
        dto.OrdenCompraFolio.Should().Be("OC-MID2026-000050");
        dto.Lineas.Should().HaveCount(3);
        dto.Lineas.Where(l => l.ArticuloId == ArtA)
            .Should().OnlyContain(l => l.ArticuloDescripcion == "Tornillo M6" && l.ArticuloClave == "ACC-001");
        dto.Lineas.Single(l => l.ArticuloId == ArtB).ArticuloDescripcion.Should().Be("Tuerca M6");
    }

    [Fact]
    public async Task Cae_al_id_cuando_el_read_port_no_resuelve()
    {
        // Sin fila de SubAlmacen y con read-ports vacíos: todos los nombres null.
        await using var db = await NuevaDbConRecepcionAsync(conSubAlmacen: false);
        var handler = new ObtenerRecepcionPorIdHandler(
            db, new FakeArticuloReadPort(), new FakeOcReadPort(), new FakeCxpDocumentosReadPort());

        var dto = await handler.Handle(new ObtenerRecepcionPorIdQuery(RecepcionId(db)), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.SubAlmacenClave.Should().BeNull();
        dto.SubAlmacenNombre.Should().BeNull();
        dto.OrdenCompraFolio.Should().BeNull();
        dto.Lineas.Should().OnlyContain(l => l.ArticuloDescripcion == null && l.ArticuloClave == null);
        // Referencias CxP sin resolver: null (el frontend cae al id truncado).
        dto.CfdiUuidFiscal.Should().BeNull();
        dto.FacturaFolio.Should().BeNull();
        // Los ids siguen presentes para que el frontend pueda caer a ellos.
        dto.SubAlmacenId.Should().Be(SubId);
        dto.OrdenCompraId.Should().Be(OcId);
        dto.CfdiRecibidoId.Should().Be(CfdiId);
        dto.FacturaId.Should().Be(FactId);
    }

    [Fact]
    public async Task Resuelve_articulos_en_batch_sin_N_mas_1()
    {
        // 3 líneas, 2 artículos distintos. Debe llamarse UNA sola vez el batch
        // con los ids distintos, y NUNCA el ObtenerAsync por-id.
        await using var db = await NuevaDbConRecepcionAsync(conSubAlmacen: true);
        var articulos = new FakeArticuloReadPort();

        var handler = new ObtenerRecepcionPorIdHandler(
            db, articulos, new FakeOcReadPort(), new FakeCxpDocumentosReadPort());
        await handler.Handle(new ObtenerRecepcionPorIdQuery(RecepcionId(db)), CancellationToken.None);

        articulos.LlamadasPorIds.Should().Be(1);
        articulos.LlamadasObtener.Should().Be(0);
        articulos.UltimosIds.Should().BeEquivalentTo(new[] { ArtA, ArtB });
    }

    [Fact]
    public async Task Resuelve_uuid_fiscal_del_cfdi_y_folio_de_factura_via_puerto_CxP()
    {
        await using var db = await NuevaDbConRecepcionAsync(conSubAlmacen: true);
        var cxp = new FakeCxpDocumentosReadPort
        {
            UuidsCfdi = new() { [CfdiId] = "5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B" },
            FoliosFactura = new() { [FactId] = "A-1234" },
        };

        var handler = new ObtenerRecepcionPorIdHandler(
            db, new FakeArticuloReadPort(), new FakeOcReadPort(), cxp);
        var dto = await handler.Handle(new ObtenerRecepcionPorIdQuery(RecepcionId(db)), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.CfdiUuidFiscal.Should().Be("5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B");
        dto.FacturaFolio.Should().Be("A-1234");
    }

    // ─── Infra de test ───

    private static ArticuloLectura Art(Guid id, string clave, string descripcion) =>
        new(id, clave, descripcion, "PZA", null, null, true);

    private static Guid RecepcionId(AlmacenDbContext db) =>
        db.Movimientos.AsNoTracking().Select(m => m.Id).Single();

    private static readonly Guid UbicId = Guid.NewGuid();

    private static async Task<AlmacenDbContext> NuevaDbConRecepcionAsync(bool conSubAlmacen)
    {
        // PR6a: InMemory con ToInMemoryQuery para la vista v_movimiento_sub_almacen.
        var opts = new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-recepcion-detalle-{Guid.NewGuid():N}")
            .Options;
        var db = new InMemoryAlmacenDbContext(opts, new BypassedEmpresaContext());
        await db.Database.EnsureCreatedAsync();

        // El sub del movimiento se deriva de la ubicación de la línea.
        db.Ubicaciones.Add(new Ubicacion(UbicId, SubId, "U1", "Ubicación 1"));
        if (conSubAlmacen)
        {
            db.SubAlmacenes.Add(new SubAlmacen(
                SubId, almacenId: Guid.NewGuid(), clave: "ALM-CEN",
                nombre: "Almacén Central", tipo: TipoSubAlmacen.Insumos));
        }

        var mov = new MovimientoInventario(
            id: Guid.NewGuid(),
            tipo: TipoMovimiento.EntradaCompra,
            empresaId: Guid.NewGuid(),
            fechaMovimiento: new DateOnly(2026, 6, 1));

        mov.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), mov.Id, 1, ArtA, 2m, "PZA", 10m, ubicacionId: UbicId));
        mov.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), mov.Id, 2, ArtB, 1m, "PZA", 20m, ubicacionId: UbicId));
        mov.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), mov.Id, 3, ArtA, 3m, "PZA", 10m, ubicacionId: UbicId));

        // Vincula la recepción a una OC + CFDI (variante A) y a la factura
        // conciliada (variante B). Los métodos son internal; se invocan por
        // reflexión, mismo patrón que el test de salida.
        typeof(MovimientoInventario)
            .GetMethod("VincularRecepcionVarianteA", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(mov, new object?[] { OcId, null, CfdiId, null });
        typeof(MovimientoInventario)
            .GetMethod("ConciliarConFacturaProveedor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(mov, new object?[] { FactId });

        db.Movimientos.Add(mov);
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class FakeArticuloReadPort : IArticuloReadPort
    {
        public IReadOnlyDictionary<Guid, ArticuloLectura> PorIds { get; set; } =
            new Dictionary<Guid, ArticuloLectura>();
        public int LlamadasPorIds { get; private set; }
        public int LlamadasObtener { get; private set; }
        public Guid[] UltimosIds { get; private set; } = Array.Empty<Guid>();

        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken cancellationToken)
        {
            LlamadasObtener++;
            return Task.FromResult<ArticuloLectura?>(null);
        }

        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> articuloIds, CancellationToken cancellationToken)
        {
            LlamadasPorIds++;
            UltimosIds = articuloIds.ToArray();
            return Task.FromResult(PorIds);
        }
    }

    private sealed class FakeOcReadPort : IComprasOcReadPort
    {
        public Dictionary<Guid, string> Folios { get; set; } = new();

        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken cancellationToken) =>
            Task.FromResult<OcLectura?>(null);

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
            IReadOnlyCollection<Guid> ocIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                Folios.Where(kv => ocIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private sealed class FakeCxpDocumentosReadPort : ICxpDocumentosReadPort
    {
        public Dictionary<Guid, string> FoliosFactura { get; set; } = new();
        public Dictionary<Guid, string> UuidsCfdi { get; set; } = new();
        public Dictionary<Guid, string> FoliosNc { get; set; } = new();

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosFacturaAsync(
            IReadOnlyCollection<Guid> facturaIds, CancellationToken cancellationToken) =>
            Filtrar(FoliosFactura, facturaIds);

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerUuidsFiscalesCfdiAsync(
            IReadOnlyCollection<Guid> cfdiRecibidoIds, CancellationToken cancellationToken) =>
            Filtrar(UuidsCfdi, cfdiRecibidoIds);

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosNotaCreditoAsync(
            IReadOnlyCollection<Guid> notaCreditoIds, CancellationToken cancellationToken) =>
            Filtrar(FoliosNc, notaCreditoIds);

        private static Task<IReadOnlyDictionary<Guid, string>> Filtrar(
            Dictionary<Guid, string> fuente, IReadOnlyCollection<Guid> ids) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                fuente.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
