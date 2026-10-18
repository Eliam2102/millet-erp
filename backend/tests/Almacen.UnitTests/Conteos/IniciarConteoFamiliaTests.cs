using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Conteos;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.Conteos;

/// <summary>
/// El filtro de familia del conteo (<c>FiltroFamilia</c>) acota el snapshot
/// al tomar la foto: solo entran los saldos cuyo artículo tiene esa categoría
/// (comparación sin distinguir mayúsculas y con los espacios recortados).
/// </summary>
public class IniciarConteoFamiliaTests
{
    private static readonly Guid SubAlmacenId = Guid.NewGuid();
    private static readonly Guid ArticuloSellante = Guid.NewGuid();
    private static readonly Guid ArticuloPintura = Guid.NewGuid();

    [Fact]
    public async Task Filtro_de_familia_con_espacios_y_otra_capitalizacion_deja_solo_los_saldos_de_esa_categoria()
    {
        await using var db = await NuevaDbAsync();
        var conteo = await SembrarConteoAsync(db, familia: "sellantes ");
        var articulos = new FakeArticulos(new Dictionary<Guid, ArticuloLectura>()
        {
            [ArticuloSellante] = Lectura(ArticuloSellante, "SEL-01", "SELLANTES"),
            [ArticuloPintura] = Lectura(ArticuloPintura, "PIN-01", "PINTURAS"),
        });

        await new IniciarConteoHandler(db, new FakeUmbrales(), articulos)
            .Handle(new IniciarConteoCommand(conteo.Id), CancellationToken.None);

        conteo.Lineas.Should().ContainSingle()
            .Which.ArticuloId.Should().Be(ArticuloSellante);
    }

    [Fact]
    public async Task Sin_filtro_de_familia_el_snapshot_incluye_todos_los_saldos()
    {
        await using var db = await NuevaDbAsync();
        var conteo = await SembrarConteoAsync(db, familia: null);
        var articulos = new FakeArticulos(new Dictionary<Guid, ArticuloLectura>()
        {
            [ArticuloSellante] = Lectura(ArticuloSellante, "SEL-01", "SELLANTES"),
            [ArticuloPintura] = Lectura(ArticuloPintura, "PIN-01", "PINTURAS"),
        });

        await new IniciarConteoHandler(db, new FakeUmbrales(), articulos)
            .Handle(new IniciarConteoCommand(conteo.Id), CancellationToken.None);

        conteo.Lineas.Should().HaveCount(2);
    }

    private static async Task<ConteoInventario> SembrarConteoAsync(AlmacenDbContext db, string? familia)
    {
        var conteo = new ConteoInventario(
            id: Guid.NewGuid(),
            empresaId: Guid.NewGuid(),
            tipo: TipoConteo.Rotativo,
            fechaPlanificada: new DateOnly(2026, 10, 8),
            responsableId: Guid.NewGuid(),
            subAlmacenId: SubAlmacenId,
            filtroFamilia: familia);
        db.Conteos.Add(conteo);
        db.SaldosInventario.Add(new SaldoInventario(Guid.NewGuid(), SubAlmacenId, ArticuloSellante, 10m, 5m));
        db.SaldosInventario.Add(new SaldoInventario(Guid.NewGuid(), SubAlmacenId, ArticuloPintura, 4m, 8m));
        await db.SaveChangesAsync();
        return conteo;
    }

    private static ArticuloLectura Lectura(Guid id, string clave, string categoria) =>
        new(id, clave, "Artículo de prueba", "PZA", null, null, true, Categoria: categoria);

    private static async Task<AlmacenDbContext> NuevaDbAsync()
    {
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-iniciar-familia-{Guid.NewGuid():N}")
            .Options;
        var db = new AlmacenDbContext(opts, new BypassedEmpresaContext());
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class FakeUmbrales : IConteoUmbralesProvider
    {
        public Task<ConteoUmbrales> ObtenerAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ConteoUmbrales(5, 1000, 1000, 10000));
    }

    private sealed class FakeArticulos(IReadOnlyDictionary<Guid, ArticuloLectura> articulos) : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken cancellationToken) =>
            Task.FromResult(articulos.TryGetValue(articuloId, out var a) ? a : null);

        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> articuloIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(
                articulos.Where(kv => articuloIds.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
