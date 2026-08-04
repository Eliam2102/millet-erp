using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Saldos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.Saldos;

/// <summary>
/// Tests del enriquecimiento de nombre de artículo en la bandeja de saldos
/// (ADR-0042). Verifica que cada fila resuelve clave + descripción vía el
/// read-port, que cae al id cuando no resuelve, y que el artículo se resuelve
/// en <b>batch</b> (1 sola llamada, sin N+1). El filtro NO se toca.
/// </summary>
public class ListarSaldosHandlerTests
{
    private static readonly Guid SubA = Guid.NewGuid();
    private static readonly Guid SubB = Guid.NewGuid();
    private static readonly Guid UbicA = Guid.NewGuid();  // ubicación ÚNICA de SubA
    private static readonly Guid UbicB = Guid.NewGuid();  // ubicación ÚNICA de SubB
    private static readonly Guid ArtA = Guid.NewGuid();
    private static readonly Guid ArtB = Guid.NewGuid();

    [Fact]
    public async Task Puebla_clave_y_nombre_via_port()
    {
        await using var db = await NuevaDbConSaldosAsync();
        var articulos = new FakeArticuloReadPort
        {
            PorIds = new Dictionary<Guid, ArticuloLectura>
            {
                [ArtA] = Art(ArtA, "ACC-001", "Tornillo M6"),
                [ArtB] = Art(ArtB, "ACC-002", "Tuerca M6"),
            },
        };

        var handler = new ListarSaldosHandler(db, articulos);
        var resp = await handler.Handle(Query(), CancellationToken.None);

        resp.Items.Should().HaveCount(3);
        resp.Items.Where(i => i.ArticuloId == ArtA)
            .Should().OnlyContain(i =>
                i.ArticuloClave == "ACC-001" && i.ArticuloDescripcion == "Tornillo M6");
        resp.Items.Single(i => i.ArticuloId == ArtB).ArticuloDescripcion.Should().Be("Tuerca M6");
    }

    [Fact]
    public async Task Cae_al_id_cuando_el_read_port_no_resuelve()
    {
        // Read-port vacío: ambos campos quedan null en cada fila.
        await using var db = await NuevaDbConSaldosAsync();
        var handler = new ListarSaldosHandler(db, new FakeArticuloReadPort());

        var resp = await handler.Handle(Query(), CancellationToken.None);

        resp.Items.Should().OnlyContain(i =>
            i.ArticuloClave == null && i.ArticuloDescripcion == null);
        // El id sigue presente para que el frontend pueda caer a él.
        resp.Items.Select(i => i.ArticuloId).Should().Contain(new[] { ArtA, ArtB });
    }

    [Fact]
    public async Task Resuelve_articulos_en_batch_sin_N_mas_1()
    {
        // 3 filas, 2 artículos distintos. Una sola llamada batch con los ids
        // distintos; nunca el ObtenerAsync por-id.
        await using var db = await NuevaDbConSaldosAsync();
        var articulos = new FakeArticuloReadPort();

        var handler = new ListarSaldosHandler(db, articulos);
        await handler.Handle(Query(), CancellationToken.None);

        articulos.LlamadasPorIds.Should().Be(1);
        articulos.LlamadasObtener.Should().Be(0);
        articulos.UltimosIds.Should().BeEquivalentTo(new[] { ArtA, ArtB });
    }

    // ─── Infra de test ───

    private static ListarSaldosQuery Query() =>
        new(SubAlmacenId: null, ArticuloId: null, SoloConStock: false, Offset: 0, Limit: 50);

    private static ArticuloLectura Art(Guid id, string clave, string descripcion) =>
        new(id, clave, descripcion, "PZA", null, null, true);

    private static async Task<AlmacenDbContext> NuevaDbConSaldosAsync()
    {
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-saldos-{Guid.NewGuid():N}")
            .Options;
        var db = new AlmacenDbContext(opts, new BypassedEmpresaContext());
        await db.Database.EnsureCreatedAsync();

        // 3 filas / 2 artículos distintos: ArtA en dos sub-almacenes, ArtB en uno.
        // PK ahora (ubicacion_id, articulo_id); SubA→UbicA, SubB→UbicB (ÚNICA).
        db.SaldosInventario.Add(new SaldoInventario(UbicA, SubA, ArtA, 100m, 50m));
        db.SaldosInventario.Add(new SaldoInventario(UbicB, SubB, ArtA, 30m, 50m));
        db.SaldosInventario.Add(new SaldoInventario(UbicA, SubA, ArtB, 50m, 200m));
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

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
