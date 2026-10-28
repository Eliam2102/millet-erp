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

        var handler = new ObtenerRecepcionPorIdHandler(db, articulos, ocs, new FakeCxpDocumentosReadPort(), new CentrosReadPort());
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
            db, new FakeArticuloReadPort(), new FakeOcReadPort(), new FakeCxpDocumentosReadPort(), new CentrosReadPort());

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
            db, articulos, new FakeOcReadPort(), new FakeCxpDocumentosReadPort(), new CentrosReadPort());
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
            db, new FakeArticuloReadPort(), new FakeOcReadPort(), cxp, new CentrosReadPort());
        var dto = await handler.Handle(new ObtenerRecepcionPorIdQuery(RecepcionId(db)), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.CfdiUuidFiscal.Should().Be("5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B");
        dto.FacturaFolio.Should().Be("A-1234");
    }

    // ─── Infra de test ───

    [Fact]
    public async Task F1COM06_muestra_documentos_de_importacion_y_fraccion_por_linea()
    {
        var lineaOc = Guid.NewGuid();
        await using var db = await NuevaDbConRecepcionAsync(conSubAlmacen: true, lineaOc);
        var mov = db.Movimientos.AsNoTracking().Single();
        var imp = new RecepcionImportacion(mov.EmpresaId, OcId, "261612346000001", "BL-1", "CONT-1", "16", "A1",
            [new RecepcionImportacionPartida(lineaOc, "84719000")]);
        imp.VincularRecepcion(mov.Id);
        db.RecepcionesImportacion.Add(imp);
        await db.SaveChangesAsync();

        var handler = new ObtenerRecepcionPorIdHandler(
            db, new FakeArticuloReadPort(), new FakeOcReadPort(), new FakeCxpDocumentosReadPort(), new CentrosReadPort());
        var dto = await handler.Handle(new ObtenerRecepcionPorIdQuery(mov.Id), CancellationToken.None);

        dto!.Importacion.Should().Be(new RecepcionImportacionDetalle("261612346000001", "BL-1", "CONT-1", "16", "Manzanillo", "A1", "Importación definitiva"));
        dto.Lineas.Single(l => l.Posicion == 1).FraccionArancelaria.Should().Be("84719000");
        dto.Lineas.Where(l => l.Posicion != 1).Should().OnlyContain(l => l.FraccionArancelaria == null);
    }

    [Fact]
    public async Task Sin_importacion_el_detalle_no_trae_documentos()
    {
        await using var db = await NuevaDbConRecepcionAsync(conSubAlmacen: true);
        var handler = new ObtenerRecepcionPorIdHandler(
            db, new FakeArticuloReadPort(), new FakeOcReadPort(), new FakeCxpDocumentosReadPort(), new CentrosReadPort());
        (await handler.Handle(new ObtenerRecepcionPorIdQuery(RecepcionId(db)), CancellationToken.None))!.Importacion.Should().BeNull();
    }

    private static ArticuloLectura Art(Guid id, string clave, string descripcion) =>
        new(id, clave, descripcion, "PZA", null, true);

    private static Guid RecepcionId(AlmacenDbContext db) =>
        db.Movimientos.AsNoTracking().Select(m => m.Id).Single();

    [Fact]
    public async Task ADM08_resuelve_centro_del_departamento_sin_maquina_en_batch()
    {
        await using var db = await NuevaDbConRecepcionAsync(conSubAlmacen: true);
        var centros = new CentrosReadPort();
        var handler = new ObtenerRecepcionPorIdHandler(db, new FakeArticuloReadPort(), new FakeOcReadPort(), new FakeCxpDocumentosReadPort(), centros);
        var dto = await handler.Handle(new ObtenerRecepcionPorIdQuery(RecepcionId(db)), default);
        dto!.Lineas.Should().OnlyContain(l => l.CentroCostoId == CentroDepartamento && l.CentroCostoClave == "A" && l.CentroCostoNombre == "DEMO departamento");
        centros.Llamadas.Should().Be(1);
        centros.Ids.Should().BeEquivalentTo(new[] { CentroDepartamento });
    }

    private static readonly Guid CentroDepartamento = Guid.NewGuid();
    private sealed class CentrosReadPort : ICentroCostoReadPort
    {
        public int Llamadas { get; private set; }
        public IReadOnlyCollection<Guid> Ids { get; private set; } = [];
        public Task<IReadOnlyDictionary<Guid, Dim3Lectura>> ObtenerAsync(IReadOnlyCollection<Guid> dim3Ids, CancellationToken cancellationToken)
        {
            Llamadas++; Ids = dim3Ids;
            return Task.FromResult<IReadOnlyDictionary<Guid, Dim3Lectura>>(new Dictionary<Guid, Dim3Lectura>
            { [CentroDepartamento] = new(CentroDepartamento, "A", "DEMO departamento", false) });
        }
    }

    private static readonly Guid UbicId = Guid.NewGuid();
    private static readonly Guid AlmacenId = Guid.NewGuid();

    [Fact]
    public async Task Resuelve_rack_de_cada_linea_con_ruta_completa()
    {
        await using var db = await NuevaDbConRecepcionAsync(conSubAlmacen: true);
        var handler = new ObtenerRecepcionPorIdHandler(
            db, new FakeArticuloReadPort(), new FakeOcReadPort(), new FakeCxpDocumentosReadPort(), new CentrosReadPort());
        var dto = await handler.Handle(new ObtenerRecepcionPorIdQuery(RecepcionId(db)), CancellationToken.None);
        dto!.Lineas.Should().OnlyContain(l => l.UbicacionId == UbicId && l.UbicacionClave == "ALM › ALM-CEN · U1");
    }

    [Fact]
    public async Task ALM04_incluye_las_lineas_del_movimiento_de_revision_marcadas_en_revision()
    {
        await using var db = await NuevaDbConRecepcionAsync(conSubAlmacen: true);
        var principalId = RecepcionId(db);
        var subRevId = Guid.NewGuid();
        var rackRevId = Guid.NewGuid();
        db.SubAlmacenes.Add(new SubAlmacen(subRevId, AlmacenId, "MAT-REV", "Material en revisión", TipoSubAlmacen.MaterialEnRevision));
        db.Ubicaciones.Add(new Ubicacion(rackRevId, subRevId, "REV-01", "Revisión 1"));
        var rev = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.EntradaCompra, Guid.NewGuid(), new DateOnly(2026, 6, 1));
        rev.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), rev.Id, 1, ArtA, 2m, "PZA", 10m, comentarioLinea: "Hojas rotas", ubicacionId: rackRevId));
        typeof(MovimientoInventario).GetProperty("RecepcionOrigenId")!.SetValue(rev, principalId);
        rev.Registrar(new FolioMovimiento("M-ENT2026-000099"), Guid.NewGuid());
        db.Movimientos.Add(rev);
        await db.SaveChangesAsync();

        var handler = new ObtenerRecepcionPorIdHandler(
            db, new FakeArticuloReadPort(), new FakeOcReadPort(), new FakeCxpDocumentosReadPort(), new CentrosReadPort());
        var dto = await handler.Handle(new ObtenerRecepcionPorIdQuery(principalId), CancellationToken.None);

        dto!.Lineas.Should().HaveCount(4);
        dto.Lineas.Select(l => l.Posicion).Should().Equal(1, 2, 3, 4);
        var enRevision = dto.Lineas.Should().ContainSingle(l => l.EnRevision).Subject;
        enRevision.MotivoDano.Should().Be("Hojas rotas");
        enRevision.UbicacionId.Should().Be(rackRevId);
        dto.Lineas.Where(l => !l.EnRevision).Should().OnlyContain(l => l.MotivoDano == null);
    }

    private static async Task<AlmacenDbContext> NuevaDbConRecepcionAsync(bool conSubAlmacen, Guid? lineaOcId = null)
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
            db.Almacenes.Add(new Millet.Almacen.Domain.Catalogo.Almacen(AlmacenId, "ALM", "Almacén", Guid.NewGuid()));
            db.SubAlmacenes.Add(new SubAlmacen(
                SubId, almacenId: AlmacenId, clave: "ALM-CEN",
                nombre: "Almacén Central", tipo: TipoSubAlmacen.Insumos));
        }

        var mov = new MovimientoInventario(
            id: Guid.NewGuid(),
            tipo: TipoMovimiento.EntradaCompra,
            empresaId: Guid.NewGuid(),
            fechaMovimiento: new DateOnly(2026, 6, 1));

        mov.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), mov.Id, 1, ArtA, 2m, "PZA", 10m, ubicacionId: UbicId, centroCostoId: CentroDepartamento, lineaOcId: lineaOcId));
        mov.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), mov.Id, 2, ArtB, 1m, "PZA", 20m, ubicacionId: UbicId, centroCostoId: CentroDepartamento));
        mov.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), mov.Id, 3, ArtA, 3m, "PZA", 10m, ubicacionId: UbicId, centroCostoId: CentroDepartamento));

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

        public Task<CfdiRecibidoLectura?> ObtenerCfdiAsync(Guid cfdiRecibidoId, CancellationToken cancellationToken) =>
            Task.FromResult<CfdiRecibidoLectura?>(null);

        public Task<IReadOnlyList<FacturaLigadaRecepcionLectura>> ObtenerFacturasLigadasRecepcionAsync(
            IReadOnlyCollection<Guid> recepcionIds, IReadOnlyCollection<Guid> facturaIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FacturaLigadaRecepcionLectura>>([]);

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
