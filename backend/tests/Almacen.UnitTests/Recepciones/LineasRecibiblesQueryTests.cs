using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>
/// P-20 (86e3pc50v): la pantalla recibe pendiente, tolerancia y máximo recibible
/// calculados en servidor con el mismo límite que el registro.
/// </summary>
public class LineasRecibiblesQueryTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProveedorId = Guid.NewGuid();
    private static readonly Guid ArticuloA = Guid.NewGuid();
    private static readonly Guid ArticuloB = Guid.NewGuid();
    private static readonly Guid ArticuloC = Guid.NewGuid();
    private static readonly Guid LineaA = Guid.NewGuid();
    private static readonly Guid LineaB = Guid.NewGuid();
    private static readonly Guid LineaC = Guid.NewGuid();

    [Fact]
    public async Task Calcula_maximo_con_la_tolerancia_de_cada_articulo()
    {
        var db = NuevaDb();
        var handler = NuevoHandler(db, NuevaOc(), 0m, 5m, 12.5m);

        var lineas = await handler.Handle(new LineasRecibiblesQuery(OcId), CancellationToken.None);

        lineas.Select(l => l.MaximoRecibible).Should().Equal(100m, 105m, 112.5m);
        lineas.Select(l => l.Pendiente).Should().Equal(100m, 100m, 100m);
        lineas.Select(l => l.ToleranciaPorcentaje).Should().Equal(0m, 5m, 12.5m);
    }

    [Fact]
    public async Task Descuenta_lo_recibido_propio_de_Almacen_en_pendiente_y_maximo()
    {
        var db = NuevaDb();
        await RegistrarEntradaAsync(db, LineaA, 40m);
        var handler = NuevoHandler(db, NuevaOc(), 0m, 5m, 12.5m);

        var lineas = await handler.Handle(new LineasRecibiblesQuery(OcId), CancellationToken.None);

        lineas[0].Pendiente.Should().Be(60m);
        lineas[0].MaximoRecibible.Should().Be(60m);
        lineas[1].MaximoRecibible.Should().Be(105m);
    }

    private static readonly Guid OcId = Guid.NewGuid();

    private static OcLectura NuevaOc() => new(
        Id: OcId,
        Folio: "OC-P20-1",
        ProveedorId: ProveedorId,
        EmpresaId: EmpresaId,
        Estado: "Autorizada",
        Lineas:
        [
            new OcLineaLectura(LineaA, ArticuloA, "PZA", 100m, 0m, 10m),
            new OcLineaLectura(LineaB, ArticuloB, "PZA", 100m, 0m, 10m),
            new OcLineaLectura(LineaC, ArticuloC, "PZA", 100m, 0m, 10m),
        ]);

    private static LineasRecibiblesHandler NuevoHandler(
        InMemoryAlmacenDbContext db, OcLectura oc, decimal tolA, decimal tolB, decimal tolC)
        => new(db, new FakeCompras(oc), new FakeArticulos(new Dictionary<Guid, decimal>
        {
            [ArticuloA] = tolA,
            [ArticuloB] = tolB,
            [ArticuloC] = tolC,
        }), new P7Support.Conversion());

    private static InMemoryAlmacenDbContext NuevaDb()
    {
        var opts = new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-p20-lineas-{Guid.NewGuid():N}")
            .Options;
        return new InMemoryAlmacenDbContext(opts, new FakeEmpresa());
    }

    private static async Task RegistrarEntradaAsync(InMemoryAlmacenDbContext db, Guid lineaOcId, decimal cantidad)
    {
        var mov = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.EntradaCompra, EmpresaId, new DateOnly(2026, 10, 1));
        mov.VincularRecepcionVarianteA(OcId, null, null, "UUID-P20-1");
        mov.AgregarLinea(new LineaMovimiento(
            Guid.NewGuid(), mov.Id, 1, ArticuloA, cantidad, "PZA", 10m, lineaOcId: lineaOcId));
        mov.Registrar(FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, 2026, 1), UserId);
        db.Movimientos.Add(mov);
        await db.SaveChangesAsync();
    }

    private sealed class FakeCompras(OcLectura oc) : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken cancellationToken)
            => Task.FromResult<OcLectura?>(ocId == oc.Id ? oc : null);

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
            IReadOnlyCollection<Guid> ocIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class FakeArticulos(IReadOnlyDictionary<Guid, decimal> tolerancias) : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken cancellationToken)
            => Task.FromResult<ArticuloLectura?>(null);

        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> articuloIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(
                articuloIds.Where(tolerancias.ContainsKey).ToDictionary(
                    id => id,
                    id => new ArticuloLectura(id, "CLAVE", "Descripción", "PZA", tolerancias[id], null, true)));
    }

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
