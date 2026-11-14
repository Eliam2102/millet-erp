using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Application.Pagos;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Pagos;

/// <summary>
/// M-326 parte 2 (b): pago a proveedor con redondeo de hasta $0.99 (±). Supuestos a validar: autorización POR REDONDEO del Gerente de Área
/// (distinto de quien lo solicita), tolerancia 0.99 MXN. El pago consume la autorización una sola vez; el banco sale por el efectivo y la
/// factura se liquida con efectivo + redondeo.
/// </summary>
public sealed class RedondeoPagoProveedorM326Tests
{
    private static readonly Guid EmpresaId = Guid.NewGuid(), Solicita = Guid.NewGuid(), Gerente = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Fecha = new(2026, 11, 12);

    private sealed class Usuario(Guid id) : ICurrentUserContext { public Guid? UserId => id; public string? UserName => "t"; }
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId; public bool IsBypassed => true; public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => Ahora; }
    private sealed class Abierto : IPeriodoContablePort { public Task<bool> EstaAbiertoAsync(int a, int m, CancellationToken ct) => Task.FromResult(true); }
    private sealed class SinLimite : IElegibleFacturaReadPort { public Task<decimal> ObtenerLimiteAcumuladoAsync(Guid id, CancellationToken ct) => Task.FromResult(decimal.MaxValue); }
    private sealed class Captura : IIntegrationEventPublisher
    {
        public List<object> Eventos { get; } = [];
        public Task PublishAsync(object e, CancellationToken ct) { Eventos.Add(e); return Task.CompletedTask; }
    }
    private sealed class Prov : IProveedorBancoReadPort
    {
        public Task<ProveedorBancoDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ProveedorBancoDto?>(new(id, "PROV", "Prov SA",
            [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", "012180000000001234", true, null, "MXN", "Prov SA", true, "Activa")], true));
        public Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorBancoDto>>(new Dictionary<Guid, ProveedorBancoDto>());
    }

    private static (TesoreriaDbContext Db, CuentaBancaria Cuenta, PasivoPendientePago Pasivo) Escenario()
    {
        var db = new TesoreriaDbContext(new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase($"m326-{Guid.NewGuid():N}").Options, new Empresa());
        var cuenta = new CuentaBancaria(EmpresaId, "BBVA", "0123456789", null, "MXN");
        var pasivo = new PasivoPendientePago(EmpresaId, Guid.NewGuid(), Guid.NewGuid(), null, 1000m, 1000m, "MXN", null, new DateOnly(2026, 12, 1), null, "F-1", Ahora, "PUE");
        db.CuentasBancarias.Add(cuenta); db.PasivosPendientesPago.Add(pasivo);
        return (db, cuenta, pasivo);
    }

    private static Task<RedondeoPagoResponse> Solicitar(TesoreriaDbContext db, Guid factura, decimal redondeo) =>
        new SolicitarRedondeoPagoHandler(db, new Empresa(), new Usuario(Solicita), new Reloj())
            .Handle(new SolicitarRedondeoPagoCommand(factura, redondeo, "Centavos del banco"), default);

    private static Task<RedondeoPagoResponse> Autorizar(TesoreriaDbContext db, Guid id, Guid usuario) =>
        new AutorizarRedondeoPagoHandler(db, new Empresa(), new Usuario(usuario), new Reloj()).Handle(new AutorizarRedondeoPagoCommand(id), default);

    private static RegistrarPagoProveedorHandler Pago(TesoreriaDbContext db, Captura eventos) =>
        new(db, new Empresa(), new Usuario(Solicita), new Abierto(), new Prov(), new SinLimite(), eventos, new Reloj());

    private static RegistrarPagoProveedorCommand Cmd(CuentaBancaria c, PasivoPendientePago p, decimal importe, decimal redondeo, string folio) =>
        new(c.Id, Fecha, [new(p.FacturaProveedorId, importe, redondeo)], folio);

    [Fact]
    public async Task Redondeo_a_favor_autorizado_liquida_la_factura_el_banco_sale_por_el_efectivo_y_la_diferencia_viaja_en_el_evento()
    {
        var (db, cuenta, pasivo) = Escenario(); await using var _ = db; await db.SaveChangesAsync();
        var sol = await Solicitar(db, pasivo.FacturaProveedorId, 0.40m);
        await Autorizar(db, sol.Id, Gerente);
        var eventos = new Captura();

        await Pago(db, eventos).Handle(Cmd(cuenta, pasivo, 999.60m, 0.40m, "SPEI-R1"), default);

        (await db.MovimientosBancarios.SingleAsync()).Monto.Should().Be(999.60m, "el banco sale solo por el efectivo");
        var aplicacion = await db.AplicacionesPagoProveedor.SingleAsync();
        aplicacion.ImporteAplicado.Should().Be(1000m); aplicacion.Redondeo.Should().Be(0.40m);
        (await db.PasivosPendientesPago.SingleAsync()).SaldoPendiente.Should().Be(0m);
        var e = eventos.Eventos.OfType<PagoFacturaProveedorAplicadoIntegrationEvent>().Single();
        e.Monto.Should().Be(1000m, "CxP liquida la factura completa"); e.Redondeo.Should().Be(0.40m);
        (await db.AutorizacionesRedondeoPago.SingleAsync()).Estado.Should().Be(EstadoAutorizacionRedondeoPago.Consumida);
    }

    [Fact]
    public async Task Redondeo_en_contra_pagado_de_mas_tambien_liquida_con_redondeo_negativo()
    {
        var (db, cuenta, pasivo) = Escenario(); await using var _ = db; await db.SaveChangesAsync();
        var sol = await Solicitar(db, pasivo.FacturaProveedorId, -0.30m);
        await Autorizar(db, sol.Id, Gerente);
        var eventos = new Captura();

        await Pago(db, eventos).Handle(Cmd(cuenta, pasivo, 1000.30m, -0.30m, "SPEI-R2"), default);

        (await db.MovimientosBancarios.SingleAsync()).Monto.Should().Be(1000.30m);
        var e = eventos.Eventos.OfType<PagoFacturaProveedorAplicadoIntegrationEvent>().Single();
        e.Monto.Should().Be(1000m); e.Redondeo.Should().Be(-0.30m);
    }

    [Fact]
    public async Task Sin_autorizacion_el_pago_con_redondeo_se_rechaza()
    {
        var (db, cuenta, pasivo) = Escenario(); await using var _ = db; await db.SaveChangesAsync();
        var act = () => Pago(db, new Captura()).Handle(Cmd(cuenta, pasivo, 999.60m, 0.40m, "SPEI-R3"), default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("RPP_NO_AUTORIZADO");

        await Solicitar(db, pasivo.FacturaProveedorId, 0.40m); // solicitada, aún sin autorizar
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("RPP_NO_AUTORIZADO");
        (await db.MovimientosBancarios.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Quien_solicita_no_puede_autorizar_su_propio_redondeo()
    {
        var (db, _, pasivo) = Escenario(); await using var _ = db; await db.SaveChangesAsync();
        var sol = await Solicitar(db, pasivo.FacturaProveedorId, 0.40m);
        var act = () => Autorizar(db, sol.Id, Solicita);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("RPP_MISMO_USUARIO");
    }

    [Fact]
    public async Task La_autorizacion_es_por_el_monto_exacto_y_se_consume_una_sola_vez()
    {
        var (db, cuenta, pasivo) = Escenario(); await using var _ = db; await db.SaveChangesAsync();
        var sol = await Solicitar(db, pasivo.FacturaProveedorId, 0.40m);
        await Autorizar(db, sol.Id, Gerente);

        var otro = () => Pago(db, new Captura()).Handle(Cmd(cuenta, pasivo, 999.50m, 0.50m, "SPEI-R4"), default);
        (await otro.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("RPP_NO_AUTORIZADO");

        await Pago(db, new Captura()).Handle(Cmd(cuenta, pasivo, 999.60m, 0.40m, "SPEI-R5"), default);
        var denuevo = () => Pago(db, new Captura()).Handle(Cmd(cuenta, pasivo, 999.60m, 0.40m, "SPEI-R6"), default);
        await denuevo.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task El_redondeo_debe_cerrar_exactamente_el_saldo()
    {
        var (db, cuenta, pasivo) = Escenario(); await using var _ = db; await db.SaveChangesAsync();
        var sol = await Solicitar(db, pasivo.FacturaProveedorId, 0.40m);
        await Autorizar(db, sol.Id, Gerente);
        var act = () => Pago(db, new Captura()).Handle(Cmd(cuenta, pasivo, 500m, 0.40m, "SPEI-R7"), default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("RPP_NO_CIERRA_SALDO");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.00)]
    [InlineData(-1.50)]
    [InlineData(0.456)]
    public async Task El_redondeo_fuera_de_099_o_con_mas_de_dos_decimales_no_se_solicita(double redondeo)
    {
        new SolicitarRedondeoPagoValidator().Validate(new SolicitarRedondeoPagoCommand(Guid.NewGuid(), (decimal)redondeo, "m"))
            .IsValid.Should().BeFalse();
        var pago = new RegistrarPagoProveedorValidator().Validate(new RegistrarPagoProveedorCommand(Guid.NewGuid(), Fecha,
            [new(Guid.NewGuid(), 10m, (decimal)redondeo)], "SPEI"));
        pago.IsValid.Should().Be(redondeo == 0);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task El_gerente_puede_rechazar_y_la_solicitud_ya_no_sirve_para_pagar()
    {
        var (db, cuenta, pasivo) = Escenario(); await using var _ = db; await db.SaveChangesAsync();
        var sol = await Solicitar(db, pasivo.FacturaProveedorId, 0.40m);
        await new RechazarRedondeoPagoHandler(db, new Empresa(), new Usuario(Gerente), new Reloj())
            .Handle(new RechazarRedondeoPagoCommand(sol.Id, "No procede"), default);
        var act = () => Pago(db, new Captura()).Handle(Cmd(cuenta, pasivo, 999.60m, 0.40m, "SPEI-R8"), default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("RPP_NO_AUTORIZADO");
    }

    [Fact]
    public async Task Pago_sin_redondeo_sigue_igual_y_no_requiere_autorizacion()
    {
        var (db, cuenta, pasivo) = Escenario(); await using var _ = db; await db.SaveChangesAsync();
        var eventos = new Captura();
        await Pago(db, eventos).Handle(Cmd(cuenta, pasivo, 1000m, 0m, "SPEI-R9"), default);
        eventos.Eventos.OfType<PagoFacturaProveedorAplicadoIntegrationEvent>().Single().Redondeo.Should().Be(0m);
    }

    [Fact]
    public async Task Revertir_el_pago_con_redondeo_contra_abona_solo_el_efectivo_y_restaura_el_saldo_completo()
    {
        var (db, cuenta, pasivo) = Escenario(); await using var _ = db; await db.SaveChangesAsync();
        var sol = await Solicitar(db, pasivo.FacturaProveedorId, 0.40m);
        await Autorizar(db, sol.Id, Gerente);
        await Pago(db, new Captura()).Handle(Cmd(cuenta, pasivo, 999.60m, 0.40m, "SPEI-R10"), default);
        var pagoId = (await db.AplicacionesPagoProveedor.SingleAsync()).Id;

        await new RevertirPagoProveedorHandler(db, new Empresa(), new Usuario(Solicita), new Abierto(), new Captura(), new Reloj())
            .Handle(new RevertirPagoProveedorCommand(pagoId, "Error de captura"), default);

        var movimientos = await db.MovimientosBancarios.OrderBy(m => m.CreadoEn).ToListAsync();
        movimientos.Should().HaveCount(2);
        movimientos.Sum(m => m.Monto).Should().Be(1999.20m, "el contramovimiento es por el efectivo (999.60), no por lo aplicado");
        (await db.PasivosPendientesPago.SingleAsync()).SaldoPendiente.Should().Be(1000m);
    }

    [Fact]
    public async Task El_pago_lleva_la_cuenta_contable_de_la_cuenta_bancaria_de_Millet_y_sin_ella_no_se_inventa()
    {
        var (db, cuenta, pasivo) = Escenario(); await using var _ = db;
        var conCuenta = new CuentaBancaria(EmpresaId, "Santander", "9876543210", null, "MXN", "102.01.01.01");
        var otroPasivo = new PasivoPendientePago(EmpresaId, Guid.NewGuid(), pasivo.ProveedorId, null, 500m, 500m, "MXN", null,
            new DateOnly(2026, 12, 1), null, "F-2", Ahora, "PUE");
        db.CuentasBancarias.Add(conCuenta); db.PasivosPendientesPago.Add(otroPasivo); await db.SaveChangesAsync();
        var eventos = new Captura();

        await Pago(db, eventos).Handle(Cmd(conCuenta, otroPasivo, 500m, 0m, "SPEI-B8"), default);
        await Pago(db, eventos).Handle(Cmd(cuenta, pasivo, 1000m, 0m, "SPEI-B8-SIN"), default);

        eventos.Eventos.OfType<PagoFacturaProveedorAplicadoIntegrationEvent>().Select(e => e.CuentaContableRef)
            .Should().Equal("102.01.01.01", null);
    }
}
