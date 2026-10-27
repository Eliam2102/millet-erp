using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.AplicacionPagos;
using Millet.CuentasPorCobrar.Application.EventListeners;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Ports.Tesoreria;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.AplicacionPagos;

/// <summary>
/// F1-CXC-03 · C7.4: depósitos sin identificar (CA7.9), identificar = proponer
/// con remittance (R13), parcialidades y saldo insoluto (C7.4-a) y la
/// correlación del REP con la propuesta confirmada.
/// </summary>
public sealed class DepositosPorIdentificarTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid SucursalA = Guid.NewGuid();
    private static readonly Guid SucursalB = Guid.NewGuid();

    // --------------------------------------------------- bandeja «Sin identificar»

    [Fact]
    public async Task Bandeja_excluye_deposito_cubierto_por_propuesta_abierta_y_descuenta_la_parcial()
    {
        using var db = Db();
        AgregarFactura(db, "f-1", 100_000m);
        await db.SaveChangesAsync();
        var cubierto = Deposito(50_000m);
        var parcial = Deposito(20_000m);
        var libre = Deposito(9_000m);
        var puerto = new FakeDepositos(cubierto, parcial, libre);
        await Proponer(db, puerto, Comando(50_000m, [new("f-1", 50_000m, null)], cubierto.MovimientoBancarioId));
        await Proponer(db, puerto, Comando(15_000m, [new("f-1", 15_000m, null)], parcial.MovimientoBancarioId));

        var lista = await new DepositosPorIdentificarHandler(db, puerto).Handle(new DepositosPorIdentificarQuery(), default);

        lista.Select(d => d.MovimientoBancarioId).Should().BeEquivalentTo([parcial.MovimientoBancarioId, libre.MovimientoBancarioId]);
        lista.Single(d => d.MovimientoBancarioId == parcial.MovimientoBancarioId).Disponible.Should().Be(5_000m);
        lista.Single(d => d.MovimientoBancarioId == libre.MovimientoBancarioId).Disponible.Should().Be(9_000m);
    }

    [Fact]
    public async Task Sobrante_ya_confirmado_por_Tesoreria_no_se_reserva_dos_veces_mientras_llega_el_evento()
    {
        using var db = Db();
        AgregarFactura(db, "f-s", 100_000m);
        await db.SaveChangesAsync();
        var deposito = Deposito(50_000m);
        var propuesta = await Proponer(db, new FakeDepositos(deposito), Comando(50_000m, [new("f-s", 45_000m, null)], deposito.MovimientoBancarioId));
        // Tesorería confirmó: del movimiento quedan 5,000 (sobrante); CxC aún no recibe el evento.
        var trasConfirmar = deposito with { Disponible = 5_000m, PropuestasConfirmadas = [propuesta.Id] };

        var lista = await new DepositosPorIdentificarHandler(db, new FakeDepositos(trasConfirmar)).Handle(new DepositosPorIdentificarQuery(), default);

        lista.Should().ContainSingle().Which.Disponible.Should().Be(5_000m);
    }

    [Fact]
    public async Task Bandeja_respeta_alcance_por_sucursal()
    {
        using var db = Db();
        var deA = Deposito(1_000m, sucursalId: SucursalA);
        var deB = Deposito(2_000m, sucursalId: SucursalB);
        var sinSucursal = Deposito(3_000m, sucursalId: null);
        var puerto = new FakeDepositos(deA, deB, sinSucursal);
        var handler = new DepositosPorIdentificarHandler(db, puerto);

        var operativo = await handler.Handle(new DepositosPorIdentificarQuery { SucursalesPermitidas = [SucursalA] }, default);
        var corporativo = await handler.Handle(new DepositosPorIdentificarQuery(), default);

        operativo.Select(d => d.MovimientoBancarioId).Should().Equal(deA.MovimientoBancarioId);
        corporativo.Should().HaveCount(3);
    }

    // --------------------------------------------------- identificar = proponer

    [Fact]
    public async Task Identificar_sin_remittance_no_propone()
    {
        using var db = Db();
        AgregarFactura(db, "f-r", 10_000m);
        await db.SaveChangesAsync();
        var deposito = Deposito(10_000m);

        var act = () => Proponer(db, new FakeDepositos(deposito),
            Comando(10_000m, [new("f-r", 10_000m, null)], deposito.MovimientoBancarioId, remittance: " "));

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAP_SIN_REMITTANCE");
    }

    [Fact]
    public async Task Identificar_por_mas_del_disponible_se_rechaza()
    {
        using var db = Db();
        AgregarFactura(db, "f-d", 100_000m);
        await db.SaveChangesAsync();
        var deposito = Deposito(10_000m);

        var act = () => Proponer(db, new FakeDepositos(deposito),
            Comando(12_000m, [new("f-d", 12_000m, null)], deposito.MovimientoBancarioId));

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAP_DEPOSITO_EXCEDE_DISPONIBLE");
    }

    [Fact]
    public async Task Identificar_en_otra_moneda_se_rechaza()
    {
        using var db = Db();
        AgregarFactura(db, "f-m", 10_000m);
        await db.SaveChangesAsync();
        var deposito = Deposito(10_000m, moneda: "USD");

        var act = () => Proponer(db, new FakeDepositos(deposito),
            Comando(10_000m, [new("f-m", 10_000m, null)], deposito.MovimientoBancarioId));

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAP_DEPOSITO_MONEDA_DISTINTA");
    }

    [Fact]
    public async Task Identificar_un_deposito_que_ya_no_esta_disponible_se_rechaza()
    {
        using var db = Db();
        AgregarFactura(db, "f-n", 10_000m);
        await db.SaveChangesAsync();

        var act = () => Proponer(db, new FakeDepositos(),
            Comando(10_000m, [new("f-n", 10_000m, null)], Guid.NewGuid()));

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAP_DEPOSITO_NO_DISPONIBLE");
    }

    [Fact]
    public async Task Identificar_liga_el_movimiento_en_la_propuesta_y_en_el_evento()
    {
        using var db = Db();
        AgregarFactura(db, "f-a", 20_000m);
        AgregarFactura(db, "f-b", 20_000m);
        AgregarFactura(db, "f-c", 10_000m);
        await db.SaveChangesAsync();
        var deposito = Deposito(50_000m);
        var publisher = new FakePublisher();

        var respuesta = await Proponer(db, new FakeDepositos(deposito),
            Comando(50_000m, [new("f-a", 20_000m, null), new("f-b", 20_000m, null), new("f-c", 10_000m, null)],
                deposito.MovimientoBancarioId), publisher);

        respuesta.MovimientoBancarioId.Should().Be(deposito.MovimientoBancarioId);
        respuesta.Facturas.Should().HaveCount(3);
        var evento = publisher.Publicados.OfType<PropuestaAplicacionPagoCreadaEvent>().Single();
        evento.MovimientoBancarioId.Should().Be(deposito.MovimientoBancarioId);
    }

    // --------------------------------------------------- parcialidades (C7.4-a)

    [Fact]
    public async Task Pago_parcial_PPD_registra_parcialidad_y_saldo_insoluto_y_la_siguiente_es_la_2()
    {
        using var db = Db();
        AgregarFactura(db, "f-ppd", 1_000m);
        await db.SaveChangesAsync();

        // El número que mande el cliente se ignora: lo calcula CxC.
        var primera = await Proponer(db, new FakeDepositos(), Comando(400m, [new("f-ppd", 400m, 7)]));
        var segunda = await Proponer(db, new FakeDepositos(), Comando(300m, [new("f-ppd", 300m, null)]));

        primera.Facturas.Single().NumParcialidad.Should().Be(1);
        primera.Facturas.Single().SaldoInsoluto.Should().Be(600m);
        segunda.Facturas.Single().NumParcialidad.Should().Be(2);
        segunda.Facturas.Single().SaldoInsoluto.Should().Be(300m);

        var detalle = await new ObtenerPropuestaAplicacionHandler(db).Handle(new ObtenerPropuestaAplicacionQuery(segunda.Id), default);
        detalle.Facturas.Single().NumParcialidad.Should().Be(2);
        detalle.Facturas.Single().SaldoInsoluto.Should().Be(300m);
    }

    [Fact]
    public async Task Parcialidad_cuenta_los_pagos_ya_timbrados_en_cartera()
    {
        using var db = Db();
        var factura = AgregarFactura(db, "f-rep", 1_000m);
        factura.AplicarPago(250m);
        db.MovimientosCartera.Add(new MovimientoCartera(EmpresaId, factura.Id, TipoMovimientoCartera.Pago, Guid.NewGuid(), 250m, Ahora));
        await db.SaveChangesAsync();

        var propuesta = await Proponer(db, new FakeDepositos(), Comando(500m, [new("f-rep", 500m, null)]));

        propuesta.Facturas.Single().NumParcialidad.Should().Be(2);
        propuesta.Facturas.Single().SaldoInsoluto.Should().Be(250m);
    }

    [Fact]
    public async Task Pago_parcial_a_factura_PUE_se_rechaza_pero_el_total_si_se_propone()
    {
        using var db = Db();
        AgregarFactura(db, "f-pue", 1_000m, metodoPago: "PUE");
        await db.SaveChangesAsync();

        var parcial = () => Proponer(db, new FakeDepositos(), Comando(400m, [new("f-pue", 400m, null)]));
        await parcial.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAP_PARCIAL_NO_PPD");

        var total = await Proponer(db, new FakeDepositos(), Comando(1_000m, [new("f-pue", 1_000m, null)]));
        total.Facturas.Single().SaldoInsoluto.Should().Be(0m);
    }

    [Fact]
    public async Task C74a_parcialidad_y_saldo_insoluto_salen_de_Facturacion()
    {
        // DEMO-F-101 PPD de 20,000 con un REP previo de 8,000 timbrado en Facturación
        // (la proyección de cartera aún no lo refleja): propuesta de 5,000 → parcialidad 2 · insoluto 7,000.
        using var db = Db();
        var factura = AgregarFactura(db, "demo-f-101", 20_000m);
        await db.SaveChangesAsync();
        var facturacion = new FakeParcialidades(new() { [factura.FacturaVentaId] = new(factura.FacturaVentaId, 12_000m, 2) });

        var abiertas = await new Millet.CuentasPorCobrar.Application.Cartera.FacturasAbiertasClienteHandler(db, facturacion)
            .Handle(new(ClienteId), default);
        abiertas.Single().NumParcialidadSiguiente.Should().Be(2);
        abiertas.Single().Saldo.Should().Be(12_000m);

        var propuesta = await Proponer(db, new FakeDepositos(), Comando(5_000m, [new("demo-f-101", 5_000m, null)]), facturacion: facturacion);
        propuesta.Facturas.Single().NumParcialidad.Should().Be(2);
        propuesta.Facturas.Single().SaldoInsoluto.Should().Be(7_000m);

        // Una segunda propuesta abierta de la misma factura suma: parcialidad 3 sobre el saldo restante.
        var segunda = await Proponer(db, new FakeDepositos(), Comando(2_000m, [new("demo-f-101", 2_000m, null)]), facturacion: facturacion);
        segunda.Facturas.Single().NumParcialidad.Should().Be(3);
        segunda.Facturas.Single().SaldoInsoluto.Should().Be(5_000m);

        var detalle = await new ObtenerPropuestaAplicacionHandler(db, facturacion).Handle(new(propuesta.Id), default);
        detalle.Facturas.Single().SaldoInsoluto.Should().Be(7_000m);
    }

    // --------------------------------------------------- REP ↔ propuesta confirmada

    [Fact]
    public async Task Rep_timbrado_marca_la_propuesta_confirmada_e_ignora_la_rechazada_del_mismo_movimiento()
    {
        using var db = Db();
        var factura = AgregarFactura(db, "f-x", 1_000m);
        await db.SaveChangesAsync();
        var deposito = Deposito(1_000m);
        var puerto = new FakeDepositos(deposito);
        var resolver = new ResolverPropuestaTesoreriaHandler(db, new FakeClock());

        var rechazada = await Proponer(db, puerto, Comando(1_000m, [new("f-x", 1_000m, null)], deposito.MovimientoBancarioId));
        await resolver.Handle(new(Guid.NewGuid(), EmpresaId, rechazada.Id, Guid.NewGuid(), Ahora, null, "Monto distinto en banco"), default);
        var confirmada = await Proponer(db, puerto, Comando(1_000m, [new("f-x", 1_000m, null)], deposito.MovimientoBancarioId));
        await resolver.Handle(new(Guid.NewGuid(), EmpresaId, confirmada.Id, Guid.NewGuid(), Ahora, deposito.MovimientoBancarioId, null), default);

        await new ReciboPagoTimbradoHandler(db, new FakeClock(), NullLogger<ReciboPagoTimbradoHandler>.Instance).Handle(
            new ReciboPagoTimbradoCommand(Guid.NewGuid(), new(EmpresaId, Ahora, Guid.NewGuid(), "REP-C74", 1_000m, 0,
                [new(factura.FacturaVentaId, 1_000m, 1, "MXN", 0m)], deposito.MovimientoBancarioId)), default);

        (await db.PropuestasAplicacionPago.FindAsync(confirmada.Id))!.ReppTimbrado.Should().BeTrue();
        (await db.PropuestasAplicacionPago.FindAsync(rechazada.Id))!.ReppTimbrado.Should().BeFalse();
        factura.SaldoPendiente.Should().Be(0m);
    }

    [Fact]
    public async Task Rep_timbrado_con_propuesta_marca_esa_aunque_el_movimiento_tenga_dos_confirmadas()
    {
        using var db = Db();
        var factura = AgregarFactura(db, "f-2p", 50_000m);
        await db.SaveChangesAsync();
        var deposito = Deposito(50_000m);
        var resolver = new ResolverPropuestaTesoreriaHandler(db, new FakeClock());
        var a = await Proponer(db, new FakeDepositos(deposito), Comando(50_000m, [new("f-2p", 45_000m, null)], deposito.MovimientoBancarioId));
        await resolver.Handle(new(Guid.NewGuid(), EmpresaId, a.Id, Guid.NewGuid(), Ahora, deposito.MovimientoBancarioId, null), default);
        var sobrante = deposito with { Disponible = 5_000m, PropuestasConfirmadas = [a.Id] };
        var b = await Proponer(db, new FakeDepositos(sobrante), Comando(5_000m, [new("f-2p", 5_000m, null)], deposito.MovimientoBancarioId));
        await resolver.Handle(new(Guid.NewGuid(), EmpresaId, b.Id, Guid.NewGuid(), Ahora.AddMinutes(1), deposito.MovimientoBancarioId, null), default);

        // Llega primero el REP de B.
        await new ReciboPagoTimbradoHandler(db, new FakeClock(), NullLogger<ReciboPagoTimbradoHandler>.Instance).Handle(
            new ReciboPagoTimbradoCommand(Guid.NewGuid(), new(EmpresaId, Ahora, Guid.NewGuid(), "REP-B", 5_000m, 0,
                [new(factura.FacturaVentaId, 5_000m, 2, "MXN", 0m)], deposito.MovimientoBancarioId, b.Id)), default);

        (await db.PropuestasAplicacionPago.FindAsync(b.Id))!.ReppTimbrado.Should().BeTrue();
        (await db.PropuestasAplicacionPago.FindAsync(a.Id))!.ReppTimbrado.Should().BeFalse();
        b.Facturas.Single().NumParcialidad.Should().Be(2);
    }

    // --------------------------------------------------- helpers

    private static DepositoPorIdentificarDto Deposito(decimal disponible, string moneda = "MXN", Guid? sucursalId = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "BBVA ****6789", new DateOnly(2026, 10, 26), "SPEI-C74",
            disponible, disponible, moneda, sucursalId, null);

    private static CrearPropuestaAplicacionCommand Comando(decimal monto, IReadOnlyList<PropuestaFacturaLinea> lineas,
        Guid? movimientoBancarioId = null, string remittance = "REM-C74") =>
        new(ClienteId, "SPEI-C74", monto, "MXN", remittance, lineas, movimientoBancarioId);

    private static async Task<PropuestaAplicacionResponse> Proponer(CuentasPorCobrarDbContext db,
        IDepositosPorIdentificarReadPort depositos, CrearPropuestaAplicacionCommand command, FakePublisher? publisher = null,
        Millet.CuentasPorCobrar.Domain.Ports.Facturacion.IReppPendientesReadPort? facturacion = null)
    {
        var handler = new CrearPropuestaAplicacionHandler(db, publisher ?? new FakePublisher(), new FakeEmpresa(),
            Options.Create(new AplicacionPagosOptions()), new FakeClock(), new FakeUser(), depositos, facturacion);
        return await handler.Handle(command, default);
    }

    private static FacturaCartera AgregarFactura(CuentasPorCobrarDbContext db, string uuid, decimal total, string metodoPago = "PPD")
    {
        var factura = FacturaCartera.Crear(EmpresaId, Guid.NewGuid(), ClienteId, "VGL860910IU4", "Cliente C7.4",
            uuid, $"FV-{uuid}", total, "MXN", metodoPago, Ahora.AddDays(-30), Ahora.AddDays(15));
        db.FacturasCartera.Add(factura);
        return factura;
    }

    private static CuentasPorCobrarDbContext Db() => new(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
        .UseInMemoryDatabase($"cxc_c74_{Guid.NewGuid()}").Options, new FakeEmpresa());

    private sealed class FakeDepositos(params DepositoPorIdentificarDto[] depositos) : IDepositosPorIdentificarReadPort
    {
        public Task<IReadOnlyList<DepositoPorIdentificarDto>> ListarAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DepositoPorIdentificarDto>>(depositos);
        public Task<DepositoPorIdentificarDto?> ObtenerAsync(Guid movimientoBancarioId, CancellationToken cancellationToken) =>
            Task.FromResult(depositos.FirstOrDefault(d => d.MovimientoBancarioId == movimientoBancarioId));
    }

    private sealed class FakeParcialidades(Dictionary<Guid, Millet.CuentasPorCobrar.Domain.Ports.Facturacion.ParcialidadPpd> datos)
        : Millet.CuentasPorCobrar.Domain.Ports.Facturacion.IReppPendientesReadPort
    {
        public Task<Millet.CuentasPorCobrar.Domain.Ports.Facturacion.ReppClienteEstado> ConsultarAsync(Guid clienteId, CancellationToken cancellationToken) =>
            Task.FromResult(new Millet.CuentasPorCobrar.Domain.Ports.Facturacion.ReppClienteEstado(false, []));
        public Task<IReadOnlyDictionary<Guid, Millet.CuentasPorCobrar.Domain.Ports.Facturacion.ParcialidadPpd>> ParcialidadesAsync(
            IReadOnlyCollection<Guid> facturaVentaIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Millet.CuentasPorCobrar.Domain.Ports.Facturacion.ParcialidadPpd>>(datos);
    }

    private sealed class FakeUser : ICurrentUserContext
    {
        public Guid? UserId => Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c7");
        public string? UserName => "CxC C7.4";
    }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct)
        {
            Publicados.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
