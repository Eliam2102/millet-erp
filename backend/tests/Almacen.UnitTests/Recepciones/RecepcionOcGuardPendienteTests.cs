using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>
/// F5 (86e3pc50v): límite único de recepción y recibido propio por línea de OC.
/// </summary>
public class RecepcionOcGuardPendienteTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ArticuloId = Guid.NewGuid();
    private static readonly Guid ProveedorId = Guid.NewGuid();

    [Theory]
    [InlineData(100, 0, 0, 100)]
    [InlineData(100, 80, 0, 20)]
    [InlineData(100, 0, 40, 60)]
    [InlineData(10, 6, 0, 4)]
    [InlineData(10, 0, 3, 7)]
    public void Limite_es_el_pendiente_exacto_con_el_mayor_recibido(decimal solicitada, decimal recibidaCompras, decimal recibidaPropia, decimal esperado)
    {
        RecepcionOcGuard.Limite(solicitada, recibidaCompras, recibidaPropia).Should().Be(esperado);
    }

    [Theory]
    [InlineData(11, 0)]
    [InlineData(10.5, 0)]
    [InlineData(5, 6)]
    public async Task Guard_rechaza_mas_que_el_pendiente_con_RECEPCION_EXCEDE_PENDIENTE(decimal cantidad, decimal recibidoCompras)
    {
        var act = () => ValidarAsync(cantidad, recibidoCompras);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(act);
        Assert.Equal("RECEPCION_EXCEDE_PENDIENTE", ex.Code);
    }

    [Fact]
    public void Mensaje_de_exceso_dice_hojas_con_medida_y_la_cantidad_sin_ella()
    {
        var conMedida = new OcLineaLectura(LineaId, ArticuloId, "M2", 178.608m, 0m, 10m, ProductoAwVarianteId: Guid.NewGuid(), AnchoMm: 2440m, AltoMm: 3660m);
        RecepcionOcGuard.MensajeExcede(conMedida, 178.608m)
            .Should().Be("Excede lo pendiente de la orden de compra (20 hojas). Almacén recibe exacto.");
        var sinMedida = new OcLineaLectura(LineaId, ArticuloId, "PZA", 10m, 0m, 10m);
        RecepcionOcGuard.MensajeExcede(sinMedida, 10m)
            .Should().Be("Excede lo pendiente de la orden de compra (10). Almacén recibe exacto.");
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(4, 6)]
    [InlineData(3, 0)]
    public async Task Guard_acepta_hasta_el_pendiente_y_parciales(decimal cantidad, decimal recibidoCompras)
    {
        var validada = await ValidarAsync(cantidad, recibidoCompras);
        validada.RecibidoPorLinea.Should().ContainKey(LineaId);
    }

    private static readonly Guid LineaId = Guid.NewGuid();

    private static Task<RecepcionOcValidada> ValidarAsync(decimal cantidad, decimal recibidoCompras)
    {
        var oc = new OcLectura(Guid.NewGuid(), "OC-TOL-1", ProveedorId, EmpresaId, "Autorizada",
            [new OcLineaLectura(LineaId, ArticuloId, "PZA", 10m, recibidoCompras, 10m)]);
        var entrada = new RegistrarRecepcionLineaInput(ArticuloId, LineaId, cantidad, null, null);
        return RecepcionOcGuard.ValidarAsync(oc, [entrada], new Dictionary<Guid, decimal>(), CancellationToken.None);
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
