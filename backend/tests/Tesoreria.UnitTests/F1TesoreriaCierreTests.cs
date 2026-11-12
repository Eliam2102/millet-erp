using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Cuentas;
using Millet.Tesoreria.Application.Movimientos;
using Millet.Tesoreria.Domain.Corridas;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests;

/// <summary>Flujo F1 de Tesorería (11-nov): M-199, M-200, M-201 (duplicados), M-264 y M-265b.</summary>
public sealed class F1TesoreriaCierreTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Contador = Guid.NewGuid();
    private static readonly Guid Director = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);

    private static DatosCuentaBancaria Bbva(string clabe = "012910001234567894") =>
        new("BBVA", "0191037803", clabe, "MXN", "102.01.01", "BBVA", "Conkal", "Pagos a proveedores", "M Industria SA de CV", "Javier Canché");

    // ------------------------------------------------------------ M-199

    [Fact]
    public void M199_alta_queda_pendiente_y_solo_otra_persona_la_aprueba()
    {
        var s = SolicitudCuentaBancaria.Alta(Empresa, Bbva(), Contador, Ahora);
        s.Estado.Should().Be(EstadoSolicitudCuenta.Pendiente);

        ((Action)(() => s.Aprobar(Contador, Ahora, null))).Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CTA_SOLICITUD_MISMO_USUARIO" && e.Message == "Quien solicitó el cambio no puede autorizarlo.");

        var cuenta = s.Aprobar(Director, Ahora.AddHours(1), null)!;
        cuenta.NumeroCuenta.Should().Be("0191037803");
        cuenta.Titular.Should().Be("M Industria SA de CV");
        (s.Estado, s.ResueltoPor, s.CuentaBancariaId).Should().Be((EstadoSolicitudCuenta.Aprobada, Director, cuenta.Id));
    }

    [Fact]
    public void M199_cambio_de_clabe_no_toca_la_cuenta_hasta_aprobar_y_rechazo_exige_motivo()
    {
        var d = Bbva();
        var cuenta = new CuentaBancaria(Empresa, d.Banco, d.NumeroCuenta, d.Clabe, d.Moneda, d.CuentaContableRef, d.PerfilExtracto);
        cuenta.ActualizarInventario(d.Sucursal, d.Finalidad, d.Titular, d.Firmantes);
        var nueva = d with { Clabe = "002910700012345675" };

        var s = SolicitudCuentaBancaria.Cambio(cuenta, nueva, Contador, Ahora);
        cuenta.Clabe.Should().Be(d.Clabe);
        s.DatosVigentes!.Clabe.Should().Be(d.Clabe);
        s.DatosPropuestos.Clabe.Should().Be("002910700012345675");

        ((Action)(() => s.Rechazar(Director, " ", Ahora))).Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CTA_SOLICITUD_MOTIVO_OBLIGATORIO");
        s.Aprobar(Director, Ahora, cuenta);
        cuenta.Clabe.Should().Be("002910700012345675");
        ((Action)(() => s.Rechazar(Director, "CLABE no coincide con carta", Ahora))).Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CTA_SOLICITUD_NO_PENDIENTE");
    }

    [Fact]
    public void M199_cambio_sin_diferencias_se_rechaza()
    {
        var d = Bbva();
        var cuenta = new CuentaBancaria(Empresa, d.Banco, d.NumeroCuenta, d.Clabe, d.Moneda, d.CuentaContableRef, d.PerfilExtracto);
        cuenta.ActualizarInventario(d.Sucursal, d.Finalidad, d.Titular, d.Firmantes);
        ((Action)(() => SolicitudCuentaBancaria.Cambio(cuenta, d, Contador, Ahora))).Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CTA_SOLICITUD_SIN_CAMBIOS");
    }

    // ------------------------------------------------------------ M-200

    [Theory]
    [InlineData("102.01.01", true, true, null)]
    [InlineData("999.99", false, false, "CTA_CUENTA_CONTABLE_NO_EXISTE")]
    [InlineData("105.01", true, false, "CTA_CUENTA_CONTABLE_NO_BANCOS")]
    public async Task M200_la_cuenta_contable_existe_y_es_de_bancos(string codigo, bool existe, bool deBancos, string? error)
    {
        await using var db = Db();
        var handler = new CrearCuentaBancariaHandler(db, new EmpresaCtx(), new Usuario(Contador), new Reloj(), new Catalogo(existe, deBancos));
        var act = () => handler.Handle(new CrearCuentaBancariaCommand("BBVA", "0191037803", null, "MXN", codigo), default);
        if (error is null) (await act()).Estado.Should().Be(EstadoSolicitudCuenta.Pendiente);
        else await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == error);
    }

    // ------------------------------------------------------------ M-201

    [Fact]
    public async Task M201_ingreso_igual_a_otro_se_rechaza_salvo_que_se_confirme_con_motivo()
    {
        await using var db = Db();
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "0110374468", null, "MXN"); db.CuentasBancarias.Add(cuenta);
        await db.SaveChangesAsync();
        var handler = new RegistrarMovimientoIngresoHandler(db, new EmpresaCtx(), new Usuario(Contador), new Periodo(), new Reloj());
        var fecha = new DateOnly(2026, 11, 10);
        await handler.Handle(new RegistrarMovimientoIngresoCommand(cuenta.Id, 25_300m, fecha, "SPEI 778812"), default);

        var duplicado = () => handler.Handle(new RegistrarMovimientoIngresoCommand(cuenta.Id, 25_300m, fecha, " spei 778812 "), default);
        await duplicado.Should().ThrowAsync<ConflictException>().Where(e => e.Code == "MOV_POSIBLE_DUPLICADO");

        await handler.Handle(new RegistrarMovimientoIngresoCommand(cuenta.Id, 25_300.01m, fecha, "SPEI 778812"), default);
        var r = await handler.Handle(new RegistrarMovimientoIngresoCommand(cuenta.Id, 25_300m, fecha, "SPEI 778812",
            ConfirmarDuplicado: true, MotivoDuplicado: "Dos anticipos del mismo cliente"), default);
        (await db.MovimientosBancarios.SingleAsync(m => m.Id == r.Id)).MotivoDuplicado.Should().StartWith("Dos anticipos del mismo cliente");
    }

    [Fact]
    public void M201_confirmar_duplicado_sin_motivo_no_pasa_la_validacion()
    {
        var v = new RegistrarMovimientoIngresoValidator().Validate(new RegistrarMovimientoIngresoCommand(
            Guid.NewGuid(), 25_300m, new DateOnly(2026, 11, 10), "SPEI 778812", ConfirmarDuplicado: true));
        v.Errors.Should().Contain(e => e.ErrorMessage == "El motivo es obligatorio.");
    }

    // ------------------------------------------------------------ M-264

    [Fact]
    public void M264_pago_parcial_no_excede_el_saldo_y_recalcula_desglose()
    {
        var pasivo = new PasivoPendientePago(Empresa, Guid.NewGuid(), Guid.NewGuid(), null, 1_053.33m, 1_053.33m, "MXN", null,
            new DateOnly(2026, 11, 20), null, "F-1", Ahora, "PUE", 1_000m, 160m, 106.67m);
        var corrida = CorridaPago.Crear(Empresa, null, Guid.NewGuid(), "MXN", Contador, Ahora);
        corrida.AgregarLinea(pasivo);

        ((Action)(() => corrida.AjustarImporte(pasivo, 1_053.34m))).Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CORRIDA_IMPORTE_INVALIDO");
        ((Action)(() => corrida.AjustarImporte(pasivo, 0m))).Should().Throw<BusinessRuleException>();

        corrida.AjustarImporte(pasivo, 500m);
        var linea = corrida.Lineas.Single();
        (linea.ImporteProgramado, corrida.Total).Should().Be((500m, 500m));
        (linea.Bruto, linea.Retenciones).Should().Be(pasivo.Desglose(500m));
    }

    // ------------------------------------------------------------ M-265b

    [Fact]
    public void M265b_tc_capturado_a_mano_exige_motivo()
    {
        var v = new Millet.Tesoreria.Application.Pagos.RegistrarPagoProveedorValidator();
        var sin = new Millet.Tesoreria.Application.Pagos.RegistrarPagoProveedorCommand(Guid.NewGuid(), new DateOnly(2026, 11, 10),
            [new(Guid.NewGuid(), 100m)], "FOLIO-1", TipoCambioPago: 18.6297m);
        v.Validate(sin).Errors.Should().Contain(e => e.PropertyName == "MotivoTipoCambio");
        v.Validate(sin with { MotivoTipoCambio = "TC que aplicó el banco en el comprobante" }).IsValid.Should().BeTrue();
        v.Validate(sin with { TipoCambioPago = null }).IsValid.Should().BeTrue();
    }

    // ------------------------------------------------------------ dobles

    private static TesoreriaDbContext Db() =>
        new(new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaCtx());

    private sealed class EmpresaCtx : ICurrentEmpresaContext
    { public Guid? Current => Empresa; public bool IsBypassed => false; public IDisposable Bypass() => new Noop(); private sealed class Noop : IDisposable { public void Dispose() { } } }
    private sealed class Usuario(Guid id) : ICurrentUserContext { public Guid? UserId => id; public string? UserName => "Prueba F1"; }
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => Ahora; }
    private sealed class Periodo : IPeriodoContablePort { public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true); }

    private sealed class Catalogo(bool existe, bool deBancos) : ICuentaContableReadPort
    {
        private CuentaContableValidacion V(string codigo) => existe
            ? new(true, null, new(Guid.NewGuid(), codigo, "Cuenta", null, TipoCuenta.Afectable, true, CuentaControl.Ninguna, false))
            : new(false, MotivoRechazoCuenta.NoExiste, null);
        public Task<CuentaContableValidacion> ValidarParaMovimientoAsync(string codigoCuenta, OrigenMovimiento origen, CancellationToken ct) => Task.FromResult(V(codigoCuenta));
        public Task<CuentaContableValidacion> ValidarParaMovimientoAsync(Guid cuentaId, OrigenMovimiento origen, CancellationToken ct) => Task.FromResult(V("x"));
        public Task<CuentaContableLectura?> ObtenerAsync(Guid cuentaId, CancellationToken ct) => Task.FromResult<CuentaContableLectura?>(null);
        public Task<bool> EsDeRolAsync(string codigoCuenta, string rol, CancellationToken ct) => Task.FromResult(deBancos && rol == "BANCOS");
    }
}
