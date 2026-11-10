using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.PublicAdapters;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.ProductoTerminado.Application.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;
using Xunit;

namespace Millet.Facturacion.UnitTests.Pedidos;

/// <summary>
/// FAC-08 (86e3yt7r7) Fase 2: la salida con albarán de Producto terminado (PT5.5) llega a Facturación por el puerto
/// <see cref="IFacturacionSalidaPort"/>, sin evento: una salida por albarán y pedido, idempotente por folio. Al cancelar el albarán,
/// misma regla que el contenedor de la Fase 1: sin factura se descarta; facturada, va a revisión. Datos ficticios.
/// </summary>
public sealed class SalidasProductoTerminadoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 7, 18, 0, 0, TimeSpan.Zero);
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly ServiceProvider _sp;
    private readonly FacturacionSalidaAdapter _puerto;

    public SalidasProductoTerminadoTests()
    {
        var db = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(_empresa));
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddDbContext<FacturacionDbContext>(o => o.UseInMemoryDatabase(db));
        services.AddSingleton<IClock>(new FakeClock(Ahora));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Millet.Facturacion.Application.AssemblyMarker).Assembly));
        _sp = services.BuildServiceProvider();
        // La Carta Porte no se usa aquí: el lector fiscal del emisor no hace falta.
        _puerto = new FacturacionSalidaAdapter(_sp.GetRequiredService<ISender>(), null!, _sp.GetRequiredService<ICurrentEmpresaContext>(), Db());
    }

    private FacturacionDbContext Db() => _sp.CreateScope().ServiceProvider.GetRequiredService<FacturacionDbContext>();

    private async Task<PedidoFacturable> PedidoAsync(string numero)
    {
        await using var db = Db();
        var p = PedidoFacturable.ImportarDesdeAw(_empresa, numero, Guid.NewGuid(), Guid.NewGuid(), "DEMO cliente nacional",
            (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        p.AgregarLinea(null, "Vidrio DEMO", "30171500", "H87", 100m, 10m, 0m, false);
        p.RecalcularTotal();
        db.PedidosFacturables.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    [Fact]
    public async Task Dos_albaranes_de_60_y_40_dan_dos_salidas_y_reenviar_uno_no_lo_duplica()
    {
        var p = await PedidoAsync("DEMO-PT-1");

        await _puerto.RegistrarSalidaAsync(p.Id, "HS-20261107-AAAAA/1", [(1, 60m)], CancellationToken.None);
        await _puerto.RegistrarSalidaAsync(p.Id, "HS-20261107-BBBBB/1", [(1, 40m)], CancellationToken.None);
        await _puerto.RegistrarSalidaAsync(p.Id, "HS-20261107-AAAAA/1", [(1, 60m)], CancellationToken.None);

        await using var db = Db();
        (await db.SalidasFacturables.OrderBy(s => s.NumeroParcialidad).Select(s => new { s.NumeroParcialidad, s.Origen, s.Referencia, s.Total }).ToListAsync())
            .Select(s => (s.NumeroParcialidad, s.Origen, s.Referencia, s.Total))
            .Should().Equal((1, OrigenSalidaFacturable.ProductoTerminado, "HS-20261107-AAAAA/1", 600m),
                (2, OrigenSalidaFacturable.ProductoTerminado, "HS-20261107-BBBBB/1", 400m));
    }

    [Fact]
    public async Task Cancelar_el_albaran_antes_de_facturar_descarta_la_salida_y_libera_lo_pendiente()
    {
        var p = await PedidoAsync("DEMO-PT-2");
        await _puerto.RegistrarSalidaAsync(p.Id, "HS-20261107-CCCCC/1", [(1, 60m)], CancellationToken.None);

        var r = await _puerto.CancelarSalidaAsync("HS-20261107-CCCCC/1", "DEMO el cliente no recibió la ruta", CancellationToken.None);
        var otraVez = await _puerto.CancelarSalidaAsync("HS-20261107-CCCCC/1", "DEMO el cliente no recibió la ruta", CancellationToken.None);

        r.Should().Be(new CancelacionSalidaPt(true, true, false));
        otraVez.Should().Be(new CancelacionSalidaPt(true, true, false));
        await using (var db = Db())
        {
            var s = await db.SalidasFacturables.SingleAsync();
            s.DescartadaEn.Should().Be(Ahora);
            s.MotivoDescarte.Should().Contain("HS-20261107-CCCCC/1").And.Contain("no recibió");
            var avance = await new PedidoSalidasHandler(db).Handle(new PedidoSalidasQuery(p.Id), CancellationToken.None);
            avance.Avance.Single(x => x.Posicion == 1).EnSalidas.Should().Be(0m);
            (await db.ExcepcionesImportacion.CountAsync()).Should().Be(0);
        }

        // Las 100 piezas vuelven a estar pendientes: otro albarán puede llevarlas.
        await _puerto.RegistrarSalidaAsync(p.Id, "HS-20261108-DDDDD/1", [(1, 100m)], CancellationToken.None);
        await using var db2 = Db();
        (await db2.SalidasFacturables.Where(s => s.DescartadaEn == null).Select(s => s.Total).SingleAsync()).Should().Be(1000m);
    }

    [Fact]
    public async Task Cancelar_el_albaran_ya_facturado_conserva_la_salida_y_la_manda_a_revision()
    {
        var p = await PedidoAsync("DEMO-PT-3");
        await _puerto.RegistrarSalidaAsync(p.Id, "HS-20261107-EEEEE/1", [(1, 60m)], CancellationToken.None);
        await using (var db = Db())
        {
            var salida = await db.SalidasFacturables.SingleAsync();
            var fv = FacturaVenta.CrearBorrador(_empresa, "FV-000001", 1, Guid.NewGuid(), null, null,
                new DatosFiscalesReceptor("AAA010101AAA", "DEMO cliente nacional", "601", "97000", "G03", "MEX", false),
                new DatosFiscalesEmisor("MIL010101AAA", "Millet", "601", "76120"), "PPD", "99", "MXN", null,
                2026, 11, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
            fv.HeredarOrigenDeSustituida(p.Id, salida.Id);
            db.FacturasVenta.Add(fv);
            await db.SaveChangesAsync();
        }

        var r = await _puerto.CancelarSalidaAsync("HS-20261107-EEEEE/1", "DEMO error de carga", CancellationToken.None);
        await _puerto.CancelarSalidaAsync("HS-20261107-EEEEE/1", "DEMO error de carga", CancellationToken.None);

        r.Should().Be(new CancelacionSalidaPt(true, false, true));
        await using var db2 = Db();
        (await db2.SalidasFacturables.SingleAsync()).Descartada.Should().BeFalse();
        var excepcion = await db2.ExcepcionesImportacion.SingleAsync();
        excepcion.PedidoRef.Should().Be("DEMO-PT-3");
        excepcion.Motivo.Should().Be(MotivoExcepcion.CancelacionSobreFacturado);
        excepcion.Detalle.Should().Contain("HS-20261107-EEEEE/1").And.Contain("FV-000001");
    }

    [Fact]
    public async Task Cancelar_un_albaran_sin_salida_no_hace_nada()
    {
        var r = await _puerto.CancelarSalidaAsync("HS-20261107-ZZZZZ/1", "DEMO", CancellationToken.None);

        r.Should().Be(new CancelacionSalidaPt(false, false, false));
        await using var db = Db();
        (await db.SalidasFacturables.CountAsync()).Should().Be(0);
    }
}
