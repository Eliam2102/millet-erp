using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.ComercioExterior.Application;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure;
using Xunit;

namespace Millet.Facturacion.UnitTests.Pedidos;

/// <summary>
/// FAC-08 (86e3qdddf) Fase 1: el contenedor embarcado en Comercio Exterior llega a Facturación como una salida
/// facturable por pedido. Se prueba con el JSON que produce el evento real de CE. Datos ficticios.
/// </summary>
public sealed class SalidasContenedorTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 4, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoy = new(2026, 11, 4);
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly string _db = Guid.NewGuid().ToString();
    private readonly ServiceProvider _sp;
    private readonly ILocalIntegrationEventConsumer _consumidor;

    public SalidasContenedorTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(_empresa));
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddDbContext<FacturacionDbContext>(o => o.UseInMemoryDatabase(_db));
        services.AddSingleton<IClock>(new FakeClock(Ahora));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Millet.Facturacion.Application.AssemblyMarker).Assembly));
        _sp = services.BuildServiceProvider();
        _consumidor = new ComercioExteriorEventListenerWorker(null!, _sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ComercioExteriorEventListenerWorker>.Instance);
    }

    private FacturacionDbContext Db() => _sp.CreateScope().ServiceProvider.GetRequiredService<FacturacionDbContext>();

    private async Task<PedidoFacturable> PedidoAsync(string numero, decimal cantidad = 100m, decimal precio = 10m)
    {
        await using var db = Db();
        var p = PedidoFacturable.ImportarDesdeAw(_empresa, numero, Guid.NewGuid(), Guid.NewGuid(), "DEMO cliente exportación",
            (short)1, ComportamientoFiscal.ExportacionConCce, "USD", null, null, null, 1, "15");
        p.AgregarLinea(null, "Vidrio DEMO", "30171500", "H87", cantidad, precio, 0m, false);
        p.AgregarLinea(null, "Canto DEMO", "30171500", "H87", 10m, 5m, 0m, false);
        p.RecalcularTotal();
        db.PedidosFacturables.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private static PedidoContenedorEvento Linea(PedidoFacturable p, params (int Pos, decimal Cant)[] lineas) =>
        new(p.Id, p.NumeroPedido!, lineas.Select(l => new LineaContenedorEvento(l.Pos, l.Cant, "H87")).ToArray());

    private static ContenedorEmbarcadoEvent Embarcado(Guid empresa, Guid contenedor, string numero, params PedidoContenedorEvento[] pedidos) =>
        new(empresa, Ahora, Guid.NewGuid(), "CE-EXP-DEMO", contenedor, numero, "DEMO-BL", Hoy, pedidos);

    private Task EntregarAsync(IntegrationEvent ev) =>
        _consumidor.ConsumirLocalAsync(ev.EventType, Guid.NewGuid(), JsonSerializer.Serialize(ev, ev.GetType()), CancellationToken.None);

    [Fact]
    public async Task Un_contenedor_con_dos_pedidos_da_una_salida_por_pedido_con_el_numero_de_contenedor()
    {
        var a = await PedidoAsync("DEMO-AW-1"); var b = await PedidoAsync("DEMO-AW-2");
        var contenedor = Guid.NewGuid();

        await EntregarAsync(Embarcado(_empresa, contenedor, "DEMU1234567", Linea(a, (1, 60m), (2, 10m)), Linea(b, (1, 25m))));

        await using var db = Db();
        var salidas = await db.SalidasFacturables.Include(s => s.Lineas).OrderBy(s => s.Total).ToListAsync();
        salidas.Should().HaveCount(2);
        salidas.Should().OnlyContain(s => s.Origen == OrigenSalidaFacturable.ContenedorExportacion
            && s.Referencia == "DEMU1234567" && s.OrigenId == contenedor && s.NumeroParcialidad == 1);
        salidas.Select(s => (s.PedidoFacturableId, s.Total)).Should().Equal((b.Id, 250m), (a.Id, 650m));
    }

    [Fact]
    public async Task Pedido_en_dos_contenedores_60_y_40_da_dos_salidas_que_suman_el_pedido()
    {
        var p = await PedidoAsync("DEMO-AW-3");

        await EntregarAsync(Embarcado(_empresa, Guid.NewGuid(), "DEMU0000001", Linea(p, (1, 60m))));
        await EntregarAsync(Embarcado(_empresa, Guid.NewGuid(), "DEMU0000002", Linea(p, (1, 40m))));

        await using var db = Db();
        (await db.SalidasFacturables.OrderBy(s => s.NumeroParcialidad).Select(s => new { s.NumeroParcialidad, s.Referencia, s.Total }).ToListAsync())
            .Select(s => (s.NumeroParcialidad, s.Referencia, s.Total))
            .Should().Equal((1, "DEMU0000001", 600m), (2, "DEMU0000002", 400m));
        var avance = await new PedidoSalidasHandler(db).Handle(new PedidoSalidasQuery(p.Id), CancellationToken.None);
        avance.Avance.Single(x => x.Posicion == 1).EnSalidas.Should().Be(100m, "no se facturan 100 en cada contenedor");
    }

    [Fact]
    public async Task Reentregar_el_embarque_no_crea_otra_salida_ni_cambia_importes()
    {
        var p = await PedidoAsync("DEMO-AW-4");
        var ev = Embarcado(_empresa, Guid.NewGuid(), "DEMU0000003", Linea(p, (1, 60m)));

        await EntregarAsync(ev);
        await EntregarAsync(ev);

        await using var db = Db();
        (await db.SalidasFacturables.SingleAsync()).Total.Should().Be(600m);
        (await db.ExcepcionesImportacion.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Un_pedido_que_no_admite_la_salida_va_a_la_bandeja_y_no_detiene_a_los_demas()
    {
        var p = await PedidoAsync("DEMO-AW-5");
        var inexistente = new PedidoContenedorEvento(Guid.NewGuid(), "DEMO-AW-NOEXISTE", [new LineaContenedorEvento(1, 1m, "H87")]);
        var excede = Linea(p, (1, 101m));
        var ok = Linea(await PedidoAsync("DEMO-AW-6"), (1, 10m));

        await EntregarAsync(Embarcado(_empresa, Guid.NewGuid(), "DEMU0000004", inexistente, excede, ok));

        await using var db = Db();
        (await db.SalidasFacturables.SingleAsync()).Total.Should().Be(100m);
        var excepciones = await db.ExcepcionesImportacion.OrderBy(e => e.PedidoRef).ToListAsync();
        excepciones.Select(e => e.PedidoRef).Should().Equal("DEMO-AW-5", "DEMO-AW-NOEXISTE");
        excepciones.Should().OnlyContain(e => e.Motivo == MotivoExcepcion.Otro && e.Detalle!.Contains("DEMU0000004"));
    }

    [Fact]
    public async Task Cancelar_el_contenedor_antes_de_facturar_descarta_la_salida_y_libera_lo_pendiente()
    {
        var p = await PedidoAsync("DEMO-AW-7");
        var contenedor = Guid.NewGuid();
        var embarque = Embarcado(_empresa, contenedor, "DEMU0000005", Linea(p, (1, 60m)));
        await EntregarAsync(embarque);

        var cancelado = new ContenedorCanceladoEvent(_empresa, Ahora, embarque.ExpedienteId, "CE-EXP-DEMO", contenedor, "DEMU0000005",
            "DEMO naviera canceló el booking", [Linea(p, (1, 60m))]);
        await EntregarAsync(cancelado);
        await EntregarAsync(cancelado);  // reentrega: sin efecto
        await EntregarAsync(embarque);   // reentrega tardía del embarque: no revive la salida

        await using (var db = Db())
        {
            var s = await db.SalidasFacturables.SingleAsync();
            s.Descartada.Should().BeTrue();
            s.MotivoDescarte.Should().Contain("DEMU0000005").And.Contain("booking");
            var r = await new PedidoSalidasHandler(db).Handle(new PedidoSalidasQuery(p.Id), CancellationToken.None);
            r.Avance.Single(x => x.Posicion == 1).EnSalidas.Should().Be(0m);
            r.Salidas.Single().DescartadaEn.Should().Be(Ahora);
        }

        // Las 100 piezas vuelven a estar pendientes: otro contenedor puede llevarlas.
        await EntregarAsync(Embarcado(_empresa, Guid.NewGuid(), "DEMU0000006", Linea(p, (1, 100m))));
        await using var db2 = Db();
        (await db2.SalidasFacturables.Where(s => s.DescartadaEn == null).Select(s => new { s.NumeroParcialidad, s.Total }).SingleAsync())
            .Should().BeEquivalentTo(new { NumeroParcialidad = 2, Total = 1000m });
    }

    [Fact]
    public async Task Cancelar_el_contenedor_ya_facturado_deja_la_salida_y_la_manda_a_revision()
    {
        var p = await PedidoAsync("DEMO-AW-8");
        var contenedor = Guid.NewGuid();
        await EntregarAsync(Embarcado(_empresa, contenedor, "DEMU0000007", Linea(p, (1, 60m))));
        await using (var db = Db())
        {
            var salida = await db.SalidasFacturables.SingleAsync();
            var fv = FacturaVenta.CrearBorrador(_empresa, "FE-000001", 1, Guid.NewGuid(), null, null,
                new DatosFiscalesReceptor("AAA010101AAA", "DEMO cliente exportación", "601", "97000", "G03", "MEX", false),
                new DatosFiscalesEmisor("MIL010101AAA", "Millet", "601", "76120"), "PPD", "99", "MXN", null,
                2026, 11, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
            fv.HeredarOrigenDeSustituida(p.Id, salida.Id);
            db.FacturasVenta.Add(fv);
            await db.SaveChangesAsync();
        }

        var cancelado = new ContenedorCanceladoEvent(_empresa, Ahora, Guid.NewGuid(), "CE-EXP-DEMO", contenedor, "DEMU0000007",
            "DEMO error de captura", [Linea(p, (1, 60m))]);
        await EntregarAsync(cancelado);
        await EntregarAsync(cancelado);

        await using var db2 = Db();
        (await db2.SalidasFacturables.SingleAsync()).Descartada.Should().BeFalse();
        var excepcion = await db2.ExcepcionesImportacion.SingleAsync();
        excepcion.PedidoRef.Should().Be("DEMO-AW-8");
        excepcion.Motivo.Should().Be(MotivoExcepcion.CancelacionSobreFacturado);
        excepcion.Detalle.Should().Contain("DEMU0000007").And.Contain("FE-000001");
    }
}
