using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Cuentas;
using Millet.Tesoreria.Application.Movimientos;
using Millet.Tesoreria.Application.Pagos;
using Millet.Tesoreria.Application.PagosACuenta;
using Millet.Tesoreria.Application.Reportes;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Pagos;

public sealed class P5ReglasHandlersTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly DateOnly Fecha = new(2026, 10, 9);
    private static readonly DateTimeOffset Ahora = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Un_pago_no_admite_dos_proveedores()
    {
        await using var db = Db();
        var cuenta = Cuenta(db);
        var uno = Pasivo(db); var dos = Pasivo(db);
        await db.SaveChangesAsync();
        var eventos = new Publicador(db);
        var act = () => Pago(db, eventos).Handle(new(cuenta.Id, Fecha,
            [new(uno.FacturaProveedorId, 100), new(dos.FacturaProveedorId, 100)]), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAGO_MULTIPROVEEDOR");
        db.MovimientosBancarios.Should().BeEmpty(); eventos.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task Pasivo_no_autorizado_no_genera_pago()
    {
        await using var db = Db(); var cuenta = Cuenta(db); await db.SaveChangesAsync();
        var eventos = new Publicador(db);
        var act = () => Pago(db, eventos).Handle(new(cuenta.Id, Fecha, [new(Guid.NewGuid(), 100)]), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAGO_PASIVO_NO_AUTORIZADO");
        db.MovimientosBancarios.Should().BeEmpty(); eventos.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task Parcial_abierto_impide_otro_pago_a_cuenta()
    {
        await using var db = Db(); var cuenta = Cuenta(db); var pasivo = Pasivo(db);
        var movimiento = MovimientoBancario.RegistrarPagoACuenta(Empresa, cuenta, pasivo.ProveedorId, 500, Fecha, null, null, "Urgencia", Usuario, Ahora);
        movimiento.ActualizarEstadoAplicacion(100); db.MovimientosBancarios.Add(movimiento); await db.SaveChangesAsync();
        var handler = new RegistrarPagoACuentaHandler(db, new EmpresaContext(), new User(), new Periodo(), new Clock());
        var act = () => handler.Handle(new(cuenta.Id, 100, Fecha, "Segundo urgente", pasivo.ProveedorId), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAGO_CUENTA_ABIERTO_EXISTENTE");
    }

    [Fact]
    public void Pago_a_cuenta_sin_proveedor_es_error_de_validacion()
    {
        new RegistrarPagoACuentaValidator().Validate(new RegistrarPagoACuentaCommand(Guid.NewGuid(), 100, Fecha, "Urgencia", null))
            .Errors.Should().Contain(e => e.PropertyName == "ProveedorId");
    }

    [Fact]
    public async Task Desligar_recalcula_sin_crear_ingreso_y_permite_religar()
    {
        await using var db = Db(); var cuenta = Cuenta(db); var pasivo = Pasivo(db);
        var movimiento = MovimientoBancario.RegistrarPagoACuenta(Empresa, cuenta, pasivo.ProveedorId, 500, Fecha, null, null, "Urgencia", Usuario, Ahora);
        db.MovimientosBancarios.Add(movimiento); await db.SaveChangesAsync();
        var eventos = new Publicador(db);
        var ligar = new LigarPagoACuentaHandler(db, new EmpresaContext(), eventos, new Clock());
        await ligar.Handle(new(movimiento.Id, pasivo.FacturaProveedorId, 200), default);
        var aplicacion = await db.AplicacionesPagoProveedor.SingleAsync();
        await new DesligarPagoACuentaHandler(db, eventos, new Clock()).Handle(new(movimiento.Id, aplicacion.Id, "Liga equivocada"), default);
        db.MovimientosBancarios.Should().ContainSingle();
        movimiento.EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.NoAplicado);
        aplicacion.MotivoReversa.Should().Be("Liga equivocada");
        aplicacion.Revertida.Should().BeTrue(); pasivo.SaldoPendiente.Should().Be(1000);
        await ligar.Handle(new(movimiento.Id, pasivo.FacturaProveedorId, 100), default);
        movimiento.EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.AplicadoParcial);
    }

    [Fact]
    public async Task Reversa_conserva_motivo_en_aplicacion_y_contramovimiento()
    {
        await using var db = Db(); var cuenta = Cuenta(db); var pasivo = Pasivo(db); await db.SaveChangesAsync();
        var eventos = new Publicador(db);
        await Pago(db, eventos).Handle(new(cuenta.Id, Fecha, [new(pasivo.FacturaProveedorId, 100)]), default);
        var aplicacion = await db.AplicacionesPagoProveedor.SingleAsync();
        await new RevertirPagoProveedorHandler(db, new EmpresaContext(), new User(), new Periodo(), eventos, new Clock())
            .Handle(new(aplicacion.Id, "Pago duplicado"), default);
        aplicacion.MotivoReversa.Should().Be("Pago duplicado");
        (await db.MovimientosBancarios.SingleAsync(m => m.ContramovimientoDe != null)).MotivoReversa.Should().Be("Pago duplicado");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consulta_enmascara_cuenta_y_clabe_segun_permiso(bool permitido)
    {
        await using var db = Db(); var cuenta = Cuenta(db); cuenta.CambiarClabe("032180000118359719"); await db.SaveChangesAsync();
        var resultado = await new SaldosPorCuentaHandler(db, new Permisos(permitido)).Handle(new(), default);
        resultado.Single().NumeroCuenta.Should().Be(permitido ? cuenta.NumeroCuenta : Clabe.Enmascarar(cuenta.NumeroCuenta));
        resultado.Single().Clabe.Should().Be(permitido ? cuenta.Clabe : Clabe.Enmascarar(cuenta.Clabe!));
    }

    [Fact]
    public async Task Cuenta_duplicada_no_expone_numero_completo()
    {
        await using var db = Db(); var cuenta = Cuenta(db); await db.SaveChangesAsync();
        var handler = new CrearCuentaBancariaHandler(db, new EmpresaContext(), new Permisos(true));
        var act = () => handler.Handle(new(cuenta.Banco, cuenta.NumeroCuenta, null, "MXN"), default);
        var error = await act.Should().ThrowAsync<ConflictException>().Where(e => e.Code == "CTA_NUMERO_DUPLICADO");
        error.Which.Message.Should().Contain(Clabe.Enmascarar(cuenta.NumeroCuenta)).And.NotContain(cuenta.NumeroCuenta);
    }

    [Fact]
    public async Task Saldo_inicial_exige_permiso_y_fecha_anterior_a_movimientos()
    {
        await using var db = Db(); var cuenta = Cuenta(db);
        db.MovimientosBancarios.Add(MovimientoBancario.RegistrarIngreso(Empresa, cuenta, 100, Fecha, null, null, null, null, Usuario, Ahora));
        await db.SaveChangesAsync();
        var comando = new RegistrarSaldoInicialCommand(cuenta.Id, 1000, Fecha, "Carga P5", cuenta.Version);
        var sinPermiso = () => new RegistrarSaldoInicialHandler(db, new Permisos(false)).Handle(comando, default);
        await sinPermiso.Should().ThrowAsync<ForbiddenException>().Where(e => e.Code == "CTA_SALDO_SIN_PERMISO");
        var corteInvalido = () => new RegistrarSaldoInicialHandler(db, new Permisos(true)).Handle(comando, default);
        await corteInvalido.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CTA_CORTE_CON_MOVIMIENTOS");
        cuenta.SaldoInicial.Should().BeNull();
    }

    [Fact]
    public async Task Desligar_no_reabre_pago_si_proveedor_ya_tiene_otro_abierto()
    {
        await using var db = Db(); var cuenta = Cuenta(db); var pasivo = Pasivo(db);
        var pago = MovimientoBancario.RegistrarPagoACuenta(Empresa, cuenta, pasivo.ProveedorId, 500, Fecha, null, null, "Urgencia", Usuario, Ahora);
        db.MovimientosBancarios.Add(pago); await db.SaveChangesAsync();
        var eventos = new Publicador(db);
        await new LigarPagoACuentaHandler(db, new EmpresaContext(), eventos, new Clock()).Handle(new(pago.Id, pasivo.FacturaProveedorId, 500), default);
        var aplicacion = await db.AplicacionesPagoProveedor.SingleAsync();
        db.MovimientosBancarios.Add(MovimientoBancario.RegistrarPagoACuenta(Empresa, cuenta, pasivo.ProveedorId, 100, Fecha, null, null, "Otro urgente", Usuario, Ahora));
        await db.SaveChangesAsync();
        var act = () => new DesligarPagoACuentaHandler(db, eventos, new Clock()).Handle(new(pago.Id, aplicacion.Id, "Corrección"), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAGO_CUENTA_ABIERTO_EXISTENTE");
        aplicacion.Revertida.Should().BeFalse(); pago.EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.Aplicado);
    }

    [Fact]
    public async Task Saldo_inicial_y_reclasificacion_se_reflejan_en_flujo_separado_por_moneda()
    {
        await using var db = Db(); var mxn = Cuenta(db); var usd = Cuenta(db, "USD");
        await db.SaveChangesAsync();
        var saldo = new RegistrarSaldoInicialHandler(db, new Permisos(true));
        await saldo.Handle(new(mxn.Id, 1000, Fecha.AddDays(-2), "Carga inicial", mxn.Version), default);
        await saldo.Handle(new(usd.Id, 50, Fecha.AddDays(-2), "Carga inicial", usd.Version), default);
        var concepto = new ConceptoMovimiento("Inversión ficticia P5", ClasificacionFlujo.Inversion); db.ConceptosMovimiento.Add(concepto);
        var ingreso = MovimientoBancario.RegistrarIngreso(Empresa, mxn, 200, Fecha, null, null, null, null, Usuario, Ahora);
        db.MovimientosBancarios.Add(ingreso);
        db.MovimientosBancarios.Add(MovimientoBancario.RegistrarIngreso(Empresa, usd, 10, Fecha, null, null, null, null, Usuario, Ahora));
        await db.SaveChangesAsync();
        var handler = new FlujoEfectivoReporteHandler(db, new Clock());
        var antes = await handler.Handle(new(Fecha, Fecha), default);
        antes.Filas.Should().Contain(f => (string?)f["tipo"] == "movimiento" && (string?)f["clasificacion"] == "Sin clasificar");
        await new ReclasificarMovimientoHandler(db).Handle(new(ingreso.Id, concepto.Id, "Corrección de concepto", ingreso.Version), default);
        var reporte = await handler.Handle(new(Fecha, Fecha), default);
        reporte.Filas.Single(f => (string?)f["tipo"] == "saldoFinal" && (string?)f["moneda"] == "MXN")["neto"].Should().Be(1200m);
        reporte.Filas.Single(f => (string?)f["tipo"] == "saldoFinal" && (string?)f["moneda"] == "USD")["neto"].Should().Be(60m);
        reporte.Filas.Single(f => (string?)f["tipo"] == "movimiento" && (string?)f["moneda"] == "MXN")["clasificacion"].Should().Be("Inversión");
        reporte.Filas.Count(f => (string?)f["tipo"] == "total").Should().Be(2);
        ingreso.MotivoReclasificacion.Should().Be("Corrección de concepto");
        var repetido = () => saldo.Handle(new(mxn.Id, 0, Fecha.AddDays(-2), "Otra carga", mxn.Version), default);
        await repetido.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CTA_SALDO_INICIAL_YA_REGISTRADO");
    }

    private static TesoreriaDbContext Db() => new(new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaContext());
    private static CuentaBancaria Cuenta(TesoreriaDbContext db, string moneda = "MXN")
    { var c = new CuentaBancaria(Empresa, "Banco ficticio P5", "1234567890", null, moneda); db.CuentasBancarias.Add(c); return c; }
    private static PasivoPendientePago Pasivo(TesoreriaDbContext db)
    { var p = new PasivoPendientePago(Empresa, Guid.NewGuid(), Guid.NewGuid(), null, 1000, 1000, "MXN", null, Fecha, null, "P5", Ahora, "PUE"); db.PasivosPendientesPago.Add(p); return p; }
    private static RegistrarPagoProveedorHandler Pago(TesoreriaDbContext db, Publicador eventos) => new(db, new EmpresaContext(), new User(), new Periodo(), new Proveedores(), new ElegibleSinLimite(), eventos, new Clock());
    private sealed class EmpresaContext : ICurrentEmpresaContext
    { public Guid? Current => Empresa; public bool IsBypassed => false; public IDisposable Bypass() => new Noop(); private sealed class Noop : IDisposable { public void Dispose() { } } }
    private sealed class User : ICurrentUserContext { public Guid? UserId => Usuario; public string? UserName => "Prueba P5"; }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Ahora; }
    // P3: Tesorería no paga más que el elegible; estas pruebas no lo limitan.
    private sealed class ElegibleSinLimite : Millet.Tesoreria.Domain.Ports.IElegibleFacturaReadPort
    { public Task<decimal> ObtenerLimiteAcumuladoAsync(Guid id, CancellationToken ct) => Task.FromResult(decimal.MaxValue); }
    private sealed class Permisos(bool permitir) : ICurrentUserPermissions { public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) => ValueTask.FromResult(permitir); }
    private sealed class Periodo : IPeriodoContablePort { public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true); }
    private sealed class Proveedores : IProveedorBancoReadPort
    {
        public Task<ProveedorBancoDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ProveedorBancoDto?>(null);
        public Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<Guid, ProveedorBancoDto>>(new Dictionary<Guid, ProveedorBancoDto>());
    }
    private sealed class Publicador(TesoreriaDbContext db) : IIntegrationEventPublisher
    {
        public List<object> Eventos { get; } = [];
        public Task PublishAsync(object evento, CancellationToken ct)
        { db.ChangeTracker.HasChanges().Should().BeTrue("el evento debe publicarse antes de SaveChanges"); Eventos.Add(evento); return Task.CompletedTask; }
    }
}
