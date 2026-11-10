using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.Tesoreria.Application.Depositos;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Application.Pagos;
using Millet.Tesoreria.Application.PagosACuenta;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Pagos;

/// <summary>
/// TES-09 (86e3jvvj4) · Tesorería guarda y manda el TC del pago:
/// <list type="bullet">
///   <item>pago a proveedor, pago a cuenta (y su liga tardía) y confirmación de cobro;</item>
///   <item>en moneda distinta de MXN: el TC capturado o, si no se captura, el del catálogo K10.6 a la fecha valor;</item>
///   <item>en MXN: sin TC;</item>
///   <item>el TC queda en el movimiento y viaja como <c>TipoCambioPago</c> (aditivo) en el evento.</item>
/// </list>
/// </summary>
public sealed class TipoCambioPagoTes09Tests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid UsuarioId = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Fecha = new(2026, 11, 6);

    private static (TesoreriaDbContext Db, CuentaBancaria Cuenta, PasivoPendientePago Pasivo) Escenario(string moneda, decimal? tcFactura)
    {
        var db = new TesoreriaDbContext(
            new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase($"tesoreria-tes09-{Guid.NewGuid():N}").Options,
            new FakeEmpresa());
        var cuenta = new CuentaBancaria(EmpresaId, "BBVA", "0123456789", null, moneda);
        var pasivo = new PasivoPendientePago(EmpresaId, Guid.NewGuid(), Guid.NewGuid(), null,
            1000m, 1000m, moneda, tcFactura, new DateOnly(2026, 12, 1), null, "F-1", Ahora, "PUE");
        db.CuentasBancarias.Add(cuenta);
        db.PasivosPendientesPago.Add(pasivo);
        return (db, cuenta, pasivo);
    }

    private static ConCuentas Proveedor(string moneda) => new()
    {
        Cuentas = [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", "012180000000001234", true, null, moneda, "Prov SA", true, "Activa")]
    };

    private static RegistrarPagoProveedorHandler PagoHandler(TesoreriaDbContext db, Captura eventos, string moneda, ITipoCambioPagoReadPort? catalogo) =>
        new(db, new FakeEmpresa(), new FakeUser(), new Abierto(), Proveedor(moneda), new ElegibleSinLimite(), eventos, new FakeClock(),
            tiposCambio: catalogo);

    // --------------------------------------------------- Pago a proveedor

    [Fact]
    public async Task CA10_7_pago_en_usd_con_tc_capturado_lo_guarda_en_el_movimiento_y_lo_manda_en_el_evento()
    {
        var (db, cuenta, pasivo) = Escenario("USD", tcFactura: 18.00m);
        await using var _ = db;
        await db.SaveChangesAsync();
        var eventos = new Captura();

        await PagoHandler(db, eventos, "USD", new Catalogo(18.40m))
            .Handle(new RegistrarPagoProveedorCommand(cuenta.Id, Fecha, [new(pasivo.FacturaProveedorId, 1000m)], "SPEI-USD-1",
                TipoCambioPago: 18.50m), default);

        (await db.MovimientosBancarios.SingleAsync()).TipoCambio.Should().Be(18.50m);
        var e = eventos.Eventos.OfType<PagoFacturaProveedorAplicadoIntegrationEvent>().Single();
        e.TipoCambioPago.Should().Be(18.50m, "el TC que aplicó el banco tiene prioridad sobre el del catálogo");
        e.TipoCambio.Should().Be(18.00m, "el TC del pasivo (de la factura) no cambia");
    }

    [Fact]
    public async Task Pago_en_usd_sin_capturar_toma_el_tc_del_catalogo_a_la_fecha_valor()
    {
        var (db, cuenta, pasivo) = Escenario("USD", tcFactura: 18.00m);
        await using var _ = db;
        await db.SaveChangesAsync();
        var eventos = new Captura();
        var catalogo = new Catalogo(18.42m);

        await PagoHandler(db, eventos, "USD", catalogo)
            .Handle(new RegistrarPagoProveedorCommand(cuenta.Id, Fecha, [new(pasivo.FacturaProveedorId, 1000m)], "SPEI-USD-2"), default);

        catalogo.Consultas.Should().ContainSingle().Which.Should().Be(("USD", Fecha));
        (await db.MovimientosBancarios.SingleAsync()).TipoCambio.Should().Be(18.42m);
        eventos.Eventos.OfType<PagoFacturaProveedorAplicadoIntegrationEvent>().Single().TipoCambioPago.Should().Be(18.42m);
    }

    [Fact]
    public async Task Pago_en_usd_sin_captura_ni_catalogo_queda_sin_tc_y_contabilidad_lo_resuelve()
    {
        var (db, cuenta, pasivo) = Escenario("USD", tcFactura: 18.00m);
        await using var _ = db;
        await db.SaveChangesAsync();
        var eventos = new Captura();

        await PagoHandler(db, eventos, "USD", new Catalogo(null))
            .Handle(new RegistrarPagoProveedorCommand(cuenta.Id, Fecha, [new(pasivo.FacturaProveedorId, 1000m)], "SPEI-USD-3"), default);

        (await db.MovimientosBancarios.SingleAsync()).TipoCambio.Should().BeNull();
        eventos.Eventos.OfType<PagoFacturaProveedorAplicadoIntegrationEvent>().Single().TipoCambioPago.Should().BeNull();
    }

    [Fact]
    public async Task TES_09_a_pago_en_mxn_no_lleva_tc_aunque_se_capture()
    {
        var (db, cuenta, pasivo) = Escenario("MXN", tcFactura: null);
        await using var _ = db;
        await db.SaveChangesAsync();
        var eventos = new Captura();
        var catalogo = new Catalogo(18.42m);

        await PagoHandler(db, eventos, "MXN", catalogo)
            .Handle(new RegistrarPagoProveedorCommand(cuenta.Id, Fecha, [new(pasivo.FacturaProveedorId, 1000m)], "SPEI-MXN-1",
                TipoCambioPago: 18.50m), default);

        catalogo.Consultas.Should().BeEmpty();
        (await db.MovimientosBancarios.SingleAsync()).TipoCambio.Should().BeNull();
        eventos.Eventos.OfType<PagoFacturaProveedorAplicadoIntegrationEvent>().Single().TipoCambioPago.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(18.1234567)]
    public void El_tc_capturado_debe_ser_positivo_y_con_6_decimales_como_maximo(double tc)
    {
        var pago = new RegistrarPagoProveedorValidator().Validate(new RegistrarPagoProveedorCommand(Guid.NewGuid(), Fecha,
            [new(Guid.NewGuid(), 1m)], "SPEI-1", TipoCambioPago: (decimal)tc));
        pago.Errors.Should().Contain(e => e.PropertyName == "TipoCambioPago");
        var aCuenta = new RegistrarPagoACuentaValidator().Validate(new RegistrarPagoACuentaCommand(Guid.NewGuid(), 100m, Fecha, "Urgente",
            Guid.NewGuid(), "SPEI-1", TipoCambioPago: (decimal)tc));
        aCuenta.Errors.Should().Contain(e => e.PropertyName == "TipoCambioPago");
        var cobro = new ConfirmarDepositoValidator().Validate(new ConfirmarDepositoCommand(Guid.NewGuid(), Guid.NewGuid(), 1,
            TipoCambioPago: (decimal)tc));
        cobro.Errors.Should().Contain(e => e.PropertyName == "TipoCambioPago");
    }

    [Fact]
    public void El_movimiento_rechaza_un_tc_no_positivo()
    {
        var movimiento = MovimientoBancario.RegistrarPagoACuenta(EmpresaId, new CuentaBancaria(EmpresaId, "BBVA", "0123456789", null, "USD"),
            Guid.NewGuid(), 100m, Fecha, "SPEI-1", null, "urgente", UsuarioId, Ahora);
        var act = () => movimiento.FijarTipoCambio(0m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "MOV_TIPO_CAMBIO_INVALIDO");
    }

    // --------------------------------------------------- Pago a cuenta + liga tardía

    [Fact]
    public async Task Pago_a_cuenta_en_usd_guarda_el_tc_y_la_liga_tardia_lo_manda_en_el_evento()
    {
        var (db, cuenta, pasivo) = Escenario("USD", tcFactura: 18.00m);
        await using var _ = db;
        await db.SaveChangesAsync();

        var respuesta = await new RegistrarPagoACuentaHandler(db, new FakeEmpresa(), new FakeUser(), new Abierto(), new FakeClock(),
                Proveedor("USD"), new Catalogo(18.42m))
            .Handle(new RegistrarPagoACuentaCommand(cuenta.Id, 400m, Fecha, "Urgente", pasivo.ProveedorId, "SPEI-PAC-1",
                TipoCambioPago: 18.55m), default);
        (await db.MovimientosBancarios.SingleAsync(m => m.Id == respuesta.MovimientoId)).TipoCambio.Should().Be(18.55m);

        var eventos = new Captura();
        await new LigarPagoACuentaHandler(db, new FakeEmpresa(), eventos, new FakeClock(), Proveedor("USD"))
            .Handle(new LigarPagoACuentaCommand(respuesta.MovimientoId, pasivo.FacturaProveedorId, 400m), default);

        eventos.Eventos.OfType<PagoFacturaProveedorAplicadoIntegrationEvent>().Single().TipoCambioPago.Should().Be(18.55m,
            "la liga tardía manda el TC del día en que se pagó, no el del día en que se liga");
    }

    // --------------------------------------------------- Confirmación de cobro

    [Fact]
    public async Task Cobro_en_usd_con_tc_capturado_lo_guarda_y_lo_manda_en_el_evento()
    {
        var (db, movimiento, deposito) = EscenarioCobro("USD");
        await using var _ = db;
        var eventos = new Captura();

        await ConfirmarHandler(db, eventos, new Catalogo(18.40m))
            .Handle(new ConfirmarDepositoCommand(deposito.Id, movimiento.Id, deposito.Version, TipoCambioPago: 18.35m), default);

        (await db.MovimientosBancarios.SingleAsync()).TipoCambio.Should().Be(18.35m);
        eventos.Eventos.OfType<PagoClienteConfirmadoIntegrationEvent>().Single().TipoCambioPago.Should().Be(18.35m);
    }

    [Fact]
    public async Task Cobro_en_usd_sin_capturar_toma_el_tc_del_catalogo_a_la_fecha_valor_del_ingreso()
    {
        var (db, movimiento, deposito) = EscenarioCobro("USD");
        await using var _ = db;
        var eventos = new Captura();
        var catalogo = new Catalogo(18.40m);

        await ConfirmarHandler(db, eventos, catalogo)
            .Handle(new ConfirmarDepositoCommand(deposito.Id, movimiento.Id, deposito.Version), default);

        catalogo.Consultas.Should().ContainSingle().Which.Should().Be(("USD", Fecha));
        eventos.Eventos.OfType<PagoClienteConfirmadoIntegrationEvent>().Single().TipoCambioPago.Should().Be(18.40m);
    }

    [Fact]
    public async Task TES_09_a_cobro_en_mxn_no_lleva_tc()
    {
        var (db, movimiento, deposito) = EscenarioCobro("MXN");
        await using var _ = db;
        var eventos = new Captura();
        var catalogo = new Catalogo(18.40m);

        await ConfirmarHandler(db, eventos, catalogo)
            .Handle(new ConfirmarDepositoCommand(deposito.Id, movimiento.Id, deposito.Version), default);

        catalogo.Consultas.Should().BeEmpty();
        eventos.Eventos.OfType<PagoClienteConfirmadoIntegrationEvent>().Single().TipoCambioPago.Should().BeNull();
    }

    /// <summary>
    /// B8 (prueba Sprints 3-4): el cobro en USD caía en la cuenta MXN del rol BANCOS. El evento ahora lleva la cuenta contable de la
    /// cuenta bancaria del depósito para que Contabilidad cargue ahí.
    /// </summary>
    [Theory]
    [InlineData("102.02.01.01")]
    [InlineData(null)]
    public async Task B8_cobro_manda_la_cuenta_contable_de_la_cuenta_bancaria_del_deposito(string? cuentaContable)
    {
        var (db, movimiento, deposito) = EscenarioCobro("USD", cuentaContable);
        await using var _ = db;
        var eventos = new Captura();

        await ConfirmarHandler(db, eventos, new Catalogo(18.40m))
            .Handle(new ConfirmarDepositoCommand(deposito.Id, movimiento.Id, deposito.Version, TipoCambioPago: 18.35m), default);

        eventos.Eventos.OfType<PagoClienteConfirmadoIntegrationEvent>().Single().CuentaContableRef.Should().Be(cuentaContable);
    }

    private static (TesoreriaDbContext Db, MovimientoBancario Movimiento, DepositoConfirmacion Deposito) EscenarioCobro(string moneda,
        string? cuentaContableRef = null)
    {
        var db = new TesoreriaDbContext(
            new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase($"tesoreria-tes09-cobro-{Guid.NewGuid():N}").Options,
            new FakeEmpresa());
        var cuenta = new CuentaBancaria(EmpresaId, "BBVA", "0123456789", null, moneda, cuentaContableRef);
        var movimiento = MovimientoBancario.RegistrarIngreso(EmpresaId, cuenta, 5_000m, Fecha, "SPEI-COBRO", null,
            BeneficiarioTipo.Cliente, Guid.NewGuid(), UsuarioId, Ahora);
        var deposito = DepositoConfirmacion.CrearDesdePropuesta(EmpresaId, Guid.NewGuid(), Guid.NewGuid(), "DEP-1", 5_000m, moneda,
            $$"""[{"FacturaVentaId":"{{Guid.NewGuid()}}","Folio":"VEN-1","ImporteAplicado":5000.00}]""", propuestoPor: Guid.NewGuid());
        db.CuentasBancarias.Add(cuenta);
        db.MovimientosBancarios.Add(movimiento);
        db.DepositosConfirmacion.Add(deposito);
        db.SaveChanges();
        return (db, movimiento, deposito);
    }

    private static ConfirmarDepositoHandler ConfirmarHandler(TesoreriaDbContext db, Captura eventos, ITipoCambioPagoReadPort catalogo) =>
        new(db, new FakeEmpresa(), new FakeUser(), eventos, new FakeClock(), tiposCambio: catalogo);

    // --------------------------------------------------- Contrato

    [Fact]
    public void El_tc_del_pago_viaja_con_el_nombre_que_lee_contabilidad_y_los_consumidores_viejos_lo_ignoran()
    {
        var outbox = new JsonSerializerOptions { WriteIndented = false };
        var listener = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var pago = new PagoFacturaProveedorAplicadoIntegrationEvent(EmpresaId, Ahora, Guid.NewGuid(), Guid.NewGuid(), 1000m, "USD", Fecha,
            "PUE", "SPEI-1", Guid.NewGuid(), 18.00m) { TipoCambioPago = 18.50m };
        var cobro = new PagoClienteConfirmadoIntegrationEvent(EmpresaId, Ahora, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            5000m, "USD", Fecha, "SPEI-2", []) { TipoCambioPago = 18.35m };

        JsonSerializer.Deserialize<ContratoContabilidad>(JsonSerializer.Serialize(pago, pago.GetType(), outbox), listener)!
            .TipoCambioPago.Should().Be(18.50m);
        JsonSerializer.Deserialize<ContratoContabilidad>(JsonSerializer.Serialize(cobro, cobro.GetType(), outbox), listener)!
            .TipoCambioPago.Should().Be(18.35m);

        // Un evento sin el campo (anterior a TES-09) se lee con null: Contabilidad cae al catálogo.
        JsonSerializer.Deserialize<ContratoContabilidad>("""{"Moneda":"USD"}""", listener)!.TipoCambioPago.Should().BeNull();
    }

    /// <summary>Espejo del contrato que lee Contabilidad (<c>DiferenciaCambiariaRealizada.TipoCambioPagoContrato</c>).</summary>
    private sealed record ContratoContabilidad(decimal? TipoCambioPago);

    // --------------------------------------------------- Fakes

    private sealed class Catalogo(decimal? valor) : ITipoCambioPagoReadPort
    {
        public List<(string Moneda, DateOnly Fecha)> Consultas { get; } = [];
        public Task<decimal?> ValorEnMxnAsync(string moneda, DateOnly fecha, CancellationToken ct)
        {
            Consultas.Add((moneda, fecha));
            return Task.FromResult(valor);
        }
    }

    private sealed class ConCuentas : IProveedorBancoReadPort
    {
        public IReadOnlyList<CuentaProveedorDto> Cuentas { get; set; } = [];
        public Task<ProveedorBancoDto?> ObtenerAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ProveedorBancoDto?>(new(id, "PROV", "Prov SA", Cuentas, true));
        public Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorBancoDto>>(new Dictionary<Guid, ProveedorBancoDto>());
    }

    private sealed class Captura : IIntegrationEventPublisher
    {
        public List<object> Eventos { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct)
        {
            Eventos.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeUser : ICurrentUserContext
    {
        public Guid? UserId => UsuarioId;
        public string? UserName => "test";
    }

    private sealed class Abierto : IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class ElegibleSinLimite : IElegibleFacturaReadPort
    {
        public Task<decimal> ObtenerLimiteAcumuladoAsync(Guid id, CancellationToken ct) => Task.FromResult(decimal.MaxValue);
    }
}
