using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>
/// P-20 (86e3pc50v): la pantalla recibe pendiente y máximo (= pendiente, sin tolerancia) recibible
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
    public async Task Maximo_recibible_es_el_pendiente_sin_tolerancia()
    {
        var db = NuevaDb();
        var handler = NuevoHandler(db, NuevaOc());

        var lineas = await handler.Handle(new LineasRecibiblesQuery(OcId), CancellationToken.None);

        lineas.Select(l => l.MaximoRecibible).Should().Equal(100m, 100m, 100m);
        lineas.Select(l => l.Pendiente).Should().Equal(100m, 100m, 100m);
    }

    [Fact]
    public async Task Descuenta_lo_recibido_propio_de_Almacen_en_pendiente_y_maximo()
    {
        var db = NuevaDb();
        await RegistrarEntradaAsync(db, LineaA, 40m);
        var handler = NuevoHandler(db, NuevaOc());

        var lineas = await handler.Handle(new LineasRecibiblesQuery(OcId), CancellationToken.None);

        lineas[0].Pendiente.Should().Be(60m);
        lineas[0].MaximoRecibible.Should().Be(60m);
        lineas[1].MaximoRecibible.Should().Be(100m);
    }

    [Fact]
    public async Task Devuelve_la_medida_de_la_linea_de_OC_y_null_si_no_la_tiene()
    {
        var variante = Guid.NewGuid();
        var oc = NuevaOc() with
        {
            Lineas =
            [
                new OcLineaLectura(LineaA, ArticuloA, "M2", 178.608m, 0m, 10m, ProductoAwVarianteId: variante, AnchoMm: 2440m, AltoMm: 3660m),
                new OcLineaLectura(LineaB, ArticuloB, "PZA", 100m, 0m, 10m),
            ],
        };

        var lineas = await NuevoHandler(NuevaDb(), oc).Handle(new LineasRecibiblesQuery(OcId), CancellationToken.None);

        lineas[0].Medida.Should().Be(new MedidaLineaOcDto(variante, 2440m, 3660m, 8.9304m));
        lineas[1].Medida.Should().BeNull();
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

    private static LineasRecibiblesHandler NuevoHandler(InMemoryAlmacenDbContext db, OcLectura oc)
        => new(db, new FakeCompras(oc), new P7Support.Conversion());

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

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
