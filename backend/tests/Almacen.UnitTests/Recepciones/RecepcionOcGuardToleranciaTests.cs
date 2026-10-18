using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>
/// F5 (86e3pc50v): límite único de recepción y recibido propio por línea de OC.
/// </summary>
public class RecepcionOcGuardToleranciaTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ArticuloId = Guid.NewGuid();
    private static readonly Guid ProveedorId = Guid.NewGuid();

    [Theory]
    [InlineData(100, 0, 0, 0, 100)]
    [InlineData(100, 0, 0, 5, 105)]
    [InlineData(100, 80, 0, 5, 25)]
    [InlineData(100, 0, 40, 0, 60)]
    [InlineData(100, 0, 100, 5, 5)]
    public void Limite_aplica_tolerancia_y_el_mayor_recibido(decimal solicitada, decimal recibidaCompras, decimal recibidaPropia, decimal tolerancia, decimal esperado)
    {
        RecepcionOcGuard.Limite(solicitada, recibidaCompras, recibidaPropia, tolerancia).Should().Be(esperado);
    }

    [Fact]
    public async Task RecibidoPorLineaOc_suma_entradas_y_resta_devoluciones_registradas()
    {
        var db = NuevaDb();
        var lineaOcId = Guid.NewGuid();
        var oc = NuevaOc(lineaOcId);

        await RegistrarEntradaAsync(db, oc, lineaOcId, 40m, 1);
        await RegistrarEntradaAsync(db, oc, lineaOcId, 60m, 2);
        var primeraLineaMov = db.LineasMovimiento.AsNoTracking().Where(l => l.LineaOcId == lineaOcId).OrderBy(l => l.Cantidad).First().Id;
        await RegistrarDevolucionAsync(db, oc, primeraLineaMov, 30m, EstadoDevolucionProveedor.Registrada);

        var conversion = new P7Support.Conversion();
        var recibido = await RecibidoPorLineaOc.CalcularAsync(db, conversion, oc, CancellationToken.None);
        recibido[lineaOcId].Should().Be(70m);

        await RegistrarDevolucionAsync(db, oc, primeraLineaMov, 10m, EstadoDevolucionProveedor.Rechazada);
        recibido = await RecibidoPorLineaOc.CalcularAsync(db, conversion, oc, CancellationToken.None);
        recibido[lineaOcId].Should().Be(70m);
    }

    private static InMemoryAlmacenDbContext NuevaDb()
    {
        var opts = new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-f5-recibido-{Guid.NewGuid():N}")
            .Options;
        return new InMemoryAlmacenDbContext(opts, new FakeEmpresa());
    }

    private static OcLectura NuevaOc(Guid lineaOcId) => new(
        Id: Guid.NewGuid(),
        Folio: "OC-TEST-1",
        ProveedorId: ProveedorId,
        EmpresaId: EmpresaId,
        Estado: "Autorizada",
        Lineas: [new OcLineaLectura(lineaOcId, ArticuloId, "PZA", 100m, 0m, 10m)]);

    private static async Task RegistrarEntradaAsync(InMemoryAlmacenDbContext db, OcLectura oc, Guid lineaOcId, decimal cantidad, int secuencial)
    {
        var mov = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.EntradaCompra, EmpresaId, new DateOnly(2026, 10, 1));
        mov.VincularRecepcionVarianteA(oc.Id, null, null, $"UUID-F5-{secuencial}");
        mov.AgregarLinea(new LineaMovimiento(
            Guid.NewGuid(), mov.Id, 1, ArticuloId, cantidad, "PZA", 10m, lineaOcId: lineaOcId));
        mov.Registrar(FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, 2026, secuencial), UserId);
        db.Movimientos.Add(mov);
        await db.SaveChangesAsync();
    }

    private static async Task RegistrarDevolucionAsync(InMemoryAlmacenDbContext db, OcLectura oc, Guid lineaMovimientoId, decimal cantidad, EstadoDevolucionProveedor estadoFinal)
    {
        var dev = new DevolucionAProveedor(Guid.NewGuid(), EmpresaId, ProveedorId, "Material no conforme", UserId, ordenCompraOrigenId: oc.Id);
        var linea = new LineaDevolucionProveedor(Guid.NewGuid(), dev.Id, 1, ArticuloId, cantidad, "PZA", 10m, lineaRecepcionOrigenId: lineaMovimientoId);
        dev.AgregarLinea(linea);
        dev.AgregarEvidencia(new EvidenciaDevolucionProveedor(Guid.NewGuid(), dev.Id, "Foto", "evidencia.jpg", "blob://evidencias/test.jpg"));
        dev.SolicitarAutorizacion();
        if (estadoFinal == EstadoDevolucionProveedor.Rechazada)
        {
            dev.Rechazar("motivo de rechazo");
        }
        else
        {
            dev.Autorizar(UserId);
            dev.MarcarRegistrada(Guid.NewGuid(), "DEV-F5-1");
        }
        db.DevolucionesProveedor.Add(dev);
        db.LineasDevolucionProveedor.Add(linea);
        await db.SaveChangesAsync();
    }

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
