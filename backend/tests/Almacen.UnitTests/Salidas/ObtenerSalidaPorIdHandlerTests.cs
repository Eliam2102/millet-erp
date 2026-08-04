using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.Salidas;

/// <summary>
/// Tests del enriquecimiento de nombres en el detalle de salida (ADR-0042).
/// Verifica que los 4 campos (sub-almacén, artículo, folio de RQ, persona
/// destinataria) resuelven a nombre, que caen al id cuando el read-port no
/// resuelve, y que el artículo se resuelve en batch (sin N+1).
/// </summary>
public class ObtenerSalidaPorIdHandlerTests
{
    private static readonly Guid SubId = Guid.NewGuid();
    private static readonly Guid ArtA = Guid.NewGuid();
    private static readonly Guid ArtB = Guid.NewGuid();
    private static readonly Guid RqId = Guid.NewGuid();
    private static readonly Guid PersonaId = Guid.NewGuid();

    [Fact]
    public async Task Resuelve_los_cuatro_campos_a_nombre()
    {
        await using var db = await NuevaDbConSalidaAsync(conSubAlmacen: true);
        var articulos = new FakeArticuloReadPort
        {
            PorIds = new Dictionary<Guid, ArticuloLectura>
            {
                [ArtA] = Art(ArtA, "ACC-001", "Tornillo M6"),
                [ArtB] = Art(ArtB, "ACC-002", "Tuerca M6"),
            },
        };
        var rqs = new FakeRequisicionReadPort { Folios = new() { [RqId] = "MID2026-000123" } };
        var usuarios = new FakeUsuarioReadPort { Nombres = new() { [PersonaId] = "Juan Pérez" } };

        var handler = new ObtenerSalidaPorIdHandler(db, articulos, rqs, usuarios, new FakeCentroCostoReadPort());
        var dto = await handler.Handle(new ObtenerSalidaPorIdQuery(SalidaId(db)), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.SubAlmacenNombre.Should().Be("Almacén Central");
        dto.SubAlmacenClave.Should().Be("ALM-CEN");
        dto.RqFolio.Should().Be("MID2026-000123");
        dto.PersonaDestinatariaNombre.Should().Be("Juan Pérez");
        dto.Lineas.Should().HaveCount(3);
        dto.Lineas.Where(l => l.ArticuloId == ArtA)
            .Should().OnlyContain(l => l.ArticuloDescripcion == "Tornillo M6" && l.ArticuloClave == "ACC-001");
        dto.Lineas.Single(l => l.ArticuloId == ArtB).ArticuloDescripcion.Should().Be("Tuerca M6");
    }

    [Fact]
    public async Task Cae_al_id_cuando_el_read_port_no_resuelve()
    {
        // Sin fila de SubAlmacen y con read-ports vacíos: todos los nombres null.
        await using var db = await NuevaDbConSalidaAsync(conSubAlmacen: false);
        var handler = new ObtenerSalidaPorIdHandler(
            db, new FakeArticuloReadPort(), new FakeRequisicionReadPort(), new FakeUsuarioReadPort(),
            new FakeCentroCostoReadPort());

        var dto = await handler.Handle(new ObtenerSalidaPorIdQuery(SalidaId(db)), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.SubAlmacenNombre.Should().BeNull();
        dto.SubAlmacenClave.Should().BeNull();
        dto.RqFolio.Should().BeNull();
        dto.PersonaDestinatariaNombre.Should().BeNull();
        dto.Lineas.Should().OnlyContain(l => l.ArticuloDescripcion == null && l.ArticuloClave == null);
        // Los ids siguen presentes para que el frontend pueda caer a ellos.
        dto.SubAlmacenId.Should().Be(SubId);
        dto.RqId.Should().Be(RqId);
        dto.PersonaDestinatariaId.Should().Be(PersonaId);
    }

    [Fact]
    public async Task Resuelve_articulos_en_batch_sin_N_mas_1()
    {
        // 3 líneas, 2 artículos distintos. Debe llamarse UNA sola vez el batch
        // con los ids distintos, y NUNCA el ObtenerAsync por-id.
        await using var db = await NuevaDbConSalidaAsync(conSubAlmacen: true);
        var articulos = new FakeArticuloReadPort();

        var handler = new ObtenerSalidaPorIdHandler(
            db, articulos, new FakeRequisicionReadPort(), new FakeUsuarioReadPort(),
            new FakeCentroCostoReadPort());
        await handler.Handle(new ObtenerSalidaPorIdQuery(SalidaId(db)), CancellationToken.None);

        articulos.LlamadasPorIds.Should().Be(1);
        articulos.LlamadasObtener.Should().Be(0);
        articulos.UltimosIds.Should().BeEquivalentTo(new[] { ArtA, ArtB });
    }

    // ─── Infra de test ───

    private static ArticuloLectura Art(Guid id, string clave, string descripcion) =>
        new(id, clave, descripcion, "PZA", null, null, true);

    private static Guid SalidaId(AlmacenDbContext db) =>
        db.Movimientos.AsNoTracking().Select(m => m.Id).Single();

    private static readonly Guid UbicId = Guid.NewGuid();

    private static async Task<AlmacenDbContext> NuevaDbConSalidaAsync(bool conSubAlmacen)
    {
        // PR6a: InMemory con ToInMemoryQuery para la vista v_movimiento_sub_almacen.
        var opts = new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-salida-detalle-{Guid.NewGuid():N}")
            .Options;
        var db = new InMemoryAlmacenDbContext(opts, new BypassedEmpresaContext());
        await db.Database.EnsureCreatedAsync();

        // El sub del movimiento se deriva de la ubicación de la línea: se siembra
        // siempre la ubicación (con su sub). El catálogo SubAlmacen sólo cuando
        // conSubAlmacen, para probar el fallback de nombres (id resuelto, nombre null).
        db.Ubicaciones.Add(new Ubicacion(UbicId, SubId, "U1", "Ubicación 1"));
        if (conSubAlmacen)
        {
            db.SubAlmacenes.Add(new SubAlmacen(
                SubId, almacenId: Guid.NewGuid(), clave: "ALM-CEN",
                nombre: "Almacén Central", tipo: TipoSubAlmacen.Insumos));
        }

        var mov = new MovimientoInventario(
            id: Guid.NewGuid(),
            tipo: TipoMovimiento.SalidaConsumo,
            empresaId: Guid.NewGuid(),
            fechaMovimiento: new DateOnly(2026, 6, 1));

        mov.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), mov.Id, 1, ArtA, 2m, "PZA", 10m, ubicacionId: UbicId));
        mov.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), mov.Id, 2, ArtB, 1m, "PZA", 20m, ubicacionId: UbicId));
        mov.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), mov.Id, 3, ArtA, 3m, "PZA", 10m, ubicacionId: UbicId));

        typeof(MovimientoInventario)
            .GetMethod("VincularSalida", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(mov, new object?[] { RqId, null, PersonaId });

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

    private sealed class FakeRequisicionReadPort : IComprasRequisicionReadPort
    {
        public Dictionary<Guid, string> Folios { get; set; } = new();

        public Task<RequisicionLectura?> ObtenerAsync(Guid rqId, CancellationToken cancellationToken) =>
            Task.FromResult<RequisicionLectura?>(null);

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
            IReadOnlyCollection<Guid> rqIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                Folios.Where(kv => rqIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private sealed class FakeUsuarioReadPort : IUsuarioReadPort
    {
        public Dictionary<Guid, string> Nombres { get; set; } = new();

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(
            IReadOnlyCollection<Guid> usuarioIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                Nombres.Where(kv => usuarioIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private sealed class FakeCentroCostoReadPort : ICentroCostoReadPort
    {
        public Dictionary<Guid, Dim3Lectura> Dim3 { get; set; } = new();

        public Task<IReadOnlyDictionary<Guid, Dim3Lectura>> ObtenerAsync(
            IReadOnlyCollection<Guid> dim3Ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Dim3Lectura>>(
                Dim3.Where(kv => dim3Ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
