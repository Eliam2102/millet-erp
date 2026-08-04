using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Conteos;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.Conteos;

/// <summary>
/// Tests del enriquecimiento de las líneas de conteo (ADR-0042): la clave y
/// descripción del artículo se resuelven en <b>batch</b> vía
/// <see cref="IArticuloReadPort"/> (una sola llamada por request, con los ids
/// distintos), la clave de sub-almacén por JOIN local, y las líneas salen
/// ordenadas por clave de artículo y rack. El artículo que el puerto no
/// resuelve cae a claves null (el FE muestra el id truncado como fallback).
/// </summary>
public class ListarLineasConteoHandlerTests
{
    private static readonly Guid ArticuloA = Guid.NewGuid();
    private static readonly Guid ArticuloB = Guid.NewGuid();

    [Fact]
    public async Task Captura_enriquece_articulo_en_batch_y_sub_almacen_por_join()
    {
        await using var db = await NuevaDbAsync();
        var conteoId = await SembrarConteoAsync(db);

        var articulos = new FakeArticuloReadPort
        {
            Articulos = new()
            {
                [ArticuloA] = Lectura(ArticuloA, "IPP60001", "Aceite de corte"),
                [ArticuloB] = Lectura(ArticuloB, "ACC86024", "PVB acústico"),
            },
        };
        var handler = new ListarLineasParaCapturarHandler(db, articulos);

        var lineas = await handler.Handle(
            new ListarLineasParaCapturarQuery(conteoId), CancellationToken.None);

        lineas.Should().HaveCount(3);
        lineas.Should().OnlyContain(l => l.SubAlmacenClave == "HG1");
        lineas.Where(l => l.ArticuloId == ArticuloA)
            .Should().OnlyContain(l => l.ArticuloClave == "IPP60001" && l.ArticuloDescripcion == "Aceite de corte");
        lineas.Single(l => l.ArticuloId == ArticuloB).ArticuloClave.Should().Be("ACC86024");

        // Orden legible: por clave de artículo y, dentro del artículo, por rack.
        lineas.Select(l => (l.ArticuloClave, l.UbicacionClave)).Should().ContainInOrder(
            ("ACC86024", "HG1-05"), ("IPP60001", "HG1-05"), ("IPP60001", "HG1-06"));

        // Batch real: UNA llamada con los ids DISTINTOS (ArticuloA aparece en
        // dos líneas pero viaja una sola vez).
        articulos.Llamadas.Should().Be(1);
        articulos.UltimosIds.Should().BeEquivalentTo(new[] { ArticuloA, ArticuloB });
    }

    [Fact]
    public async Task Captura_cae_a_claves_null_cuando_el_articulo_no_resuelve()
    {
        await using var db = await NuevaDbAsync();
        var conteoId = await SembrarConteoAsync(db);

        var handler = new ListarLineasParaCapturarHandler(db, new FakeArticuloReadPort());

        var lineas = await handler.Handle(
            new ListarLineasParaCapturarQuery(conteoId), CancellationToken.None);

        lineas.Should().HaveCount(3);
        lineas.Should().OnlyContain(l => l.ArticuloClave == null && l.ArticuloDescripcion == null);
        // La clave de sub-almacén no depende del puerto (JOIN local).
        lineas.Should().OnlyContain(l => l.SubAlmacenClave == "HG1");
    }

    [Fact]
    public async Task Comparacion_enriquece_igual_y_conserva_las_variaciones()
    {
        await using var db = await NuevaDbAsync();
        var conteoId = await SembrarConteoAsync(db, capturar: true);

        var articulos = new FakeArticuloReadPort
        {
            Articulos = new()
            {
                [ArticuloA] = Lectura(ArticuloA, "IPP60001", "Aceite de corte"),
                [ArticuloB] = Lectura(ArticuloB, "ACC86024", "PVB acústico"),
            },
        };
        var handler = new ListarLineasComparacionHandler(db, articulos);

        var lineas = await handler.Handle(
            new ListarLineasComparacionQuery(conteoId), CancellationToken.None);

        lineas.Should().HaveCount(3);
        lineas.Should().OnlyContain(l => l.SubAlmacenClave == "HG1");
        lineas.Should().OnlyContain(l => l.ArticuloClave != null);
        articulos.Llamadas.Should().Be(1);

        // El enriquecimiento no toca el cálculo de variaciones: capturamos 30
        // contra teórico 0 → variación absoluta +30.
        var capturada = lineas.Single(l => l.CantidadRealCapturada == 30m);
        capturada.VariacionAbsoluta.Should().Be(30m);
    }

    // ─── Infra de test ───

    private static ArticuloLectura Lectura(Guid id, string clave, string descripcion) =>
        new(id, clave, descripcion, "PZA", null, null, EsActivo: true);

    /// <summary>
    /// Conteo con 3 líneas en la zona HG1: ArticuloA en los racks HG1-06 y
    /// HG1-05 (insertadas en desorden a propósito) y ArticuloB en HG1-05.
    /// </summary>
    private static async Task<Guid> SembrarConteoAsync(AlmacenDbContext db, bool capturar = false)
    {
        var subAlmacenId = Guid.NewGuid();
        db.SubAlmacenes.Add(new SubAlmacen(
            subAlmacenId, almacenId: Guid.NewGuid(), clave: "HG1",
            nombre: "Zona HG1", tipo: TipoSubAlmacen.Insumos));

        var rack05 = Guid.NewGuid();
        var rack06 = Guid.NewGuid();
        db.Ubicaciones.Add(new Ubicacion(rack05, subAlmacenId, "HG1-05", "Rack HG1-05"));
        db.Ubicaciones.Add(new Ubicacion(rack06, subAlmacenId, "HG1-06", "Rack HG1-06"));

        var conteo = new ConteoInventario(
            id: Guid.NewGuid(), empresaId: Guid.NewGuid(),
            tipo: TipoConteo.Rotativo,
            fechaPlanificada: new DateOnly(2026, 7, 9),
            responsableId: Guid.NewGuid(),
            subAlmacenId: subAlmacenId);

        var lineaCapturable = new LineaConteo(
            Guid.NewGuid(), conteo.Id, ArticuloA, subAlmacenId, rack06,
            cantidadTeorica: 0m, costoPromedioSnapshot: 0m);
        conteo.AgregarLinea(lineaCapturable);
        conteo.AgregarLinea(new LineaConteo(
            Guid.NewGuid(), conteo.Id, ArticuloA, subAlmacenId, rack05,
            cantidadTeorica: 0m, costoPromedioSnapshot: 0m));
        conteo.AgregarLinea(new LineaConteo(
            Guid.NewGuid(), conteo.Id, ArticuloB, subAlmacenId, rack05,
            cantidadTeorica: 0m, costoPromedioSnapshot: 0m));

        if (capturar)
        {
            conteo.Iniciar();
            lineaCapturable.Capturar(30m, capturadoPor: Guid.NewGuid());
        }

        db.Conteos.Add(conteo);
        await db.SaveChangesAsync();
        return conteo.Id;
    }

    private static async Task<AlmacenDbContext> NuevaDbAsync()
    {
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-conteo-lineas-{Guid.NewGuid():N}")
            .Options;
        var db = new AlmacenDbContext(opts, new BypassedEmpresaContext());
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class FakeArticuloReadPort : IArticuloReadPort
    {
        public Dictionary<Guid, ArticuloLectura> Articulos { get; set; } = new();
        public int Llamadas { get; private set; }
        public Guid[] UltimosIds { get; private set; } = Array.Empty<Guid>();

        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken cancellationToken) =>
            Task.FromResult(Articulos.TryGetValue(articuloId, out var a) ? a : null);

        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> articuloIds, CancellationToken cancellationToken)
        {
            Llamadas++;
            UltimosIds = articuloIds.ToArray();
            return Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(
                Articulos.Where(kv => articuloIds.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value));
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
