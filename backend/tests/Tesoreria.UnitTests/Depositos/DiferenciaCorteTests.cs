using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Depositos;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Depositos;

/// <summary>
/// Tarea 86e3vr1tj (Millet 23-oct, Fernando Alejos): faltante o sobrante del corte a «deudores funcionarios y
/// empleados», autorizado por Tesorería. Criterios 1-6 de la ficha + integración con el depósito por fichas (#288).
/// </summary>
public sealed class DiferenciaCorteTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cajero = Guid.NewGuid();
    private static readonly Guid Tesorero = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Dia = new(2026, 10, 28);

    private static TesoreriaDbContext Nueva() => new(new DbContextOptionsBuilder<TesoreriaDbContext>()
        .UseInMemoryDatabase($"tes_dif_{Guid.NewGuid()}").Options, new FakeEmpresa());

    private static CajaSesionCerradaPayload Cierre(Guid sesion, decimal declarado, decimal diferencia, Guid? cajero = null) =>
        new(Empresa, Ahora, sesion, Guid.NewGuid(), Sucursal, Dia, declarado, cajero ?? Cajero, diferencia);

    private static async Task<DiferenciaCorte> Cerrar(TesoreriaDbContext db, decimal declarado, decimal diferencia, Guid? sesion = null)
    {
        await new ProyectarExpectativaCajaHandler(db, new FakeClock())
            .Handle(new ProyectarExpectativaCajaCommand(Guid.NewGuid(), Cierre(sesion ?? Guid.NewGuid(), declarado, diferencia)), default);
        return await db.DiferenciasCorte.SingleAsync();
    }

    private static Task<DiferenciaCorteResponse> Autorizar(TesoreriaDbContext db, FakePublisher pub, DiferenciaCorte d, Guid usuario) =>
        new AutorizarDiferenciaCorteHandler(db, new FakeUser(usuario), pub, new FakeClock())
            .Handle(new AutorizarDiferenciaCorteCommand(d.Id, d.Version), default);

    // CA1: esperado 5,000 y contado 4,950 → diferencia −50 pendiente y el corte no aparece conciliado.
    [Fact]
    public async Task CA1_corte_con_faltante_crea_diferencia_pendiente_y_no_se_concilia()
    {
        using var db = Nueva();
        var sesion = Guid.NewGuid();
        var dif = await Cerrar(db, 4_950m, -50m, sesion);

        dif.Importe.Should().Be(-50m);
        dif.EsFaltante.Should().BeTrue();
        dif.Estado.Should().Be(EstadoDiferenciaCorte.PendienteAutorizar);
        dif.CajeroId.Should().Be(Cajero);
        dif.SucursalId.Should().Be(Sucursal);
        dif.CajaSesionId.Should().Be(sesion);

        // El depósito (4,950) sí se registra, pero el corte no queda conciliado.
        var r = await ConfirmarFicha(db, 4_950m, sesion);
        r.Estado.Should().Be(EstadoDepositoConfirmacion.Confirmada);
        r.DiferenciaEstado.Should().Be(EstadoDiferenciaCorte.PendienteAutorizar);
        r.Conciliado.Should().BeFalse();
    }

    // CA2: Tesorería autoriza → evento con $50, cajero y corte; el corte puede conciliarse.
    [Fact]
    public async Task CA2_autorizar_publica_evento_y_el_corte_se_concilia()
    {
        using var db = Nueva(); var pub = new FakePublisher();
        var sesion = Guid.NewGuid();
        var dif = await Cerrar(db, 4_950m, -50m, sesion);
        await ConfirmarFicha(db, 4_950m, sesion);

        var r = await Autorizar(db, pub, dif, Tesorero);

        r.Estado.Should().Be(EstadoDiferenciaCorte.Autorizada);
        var ev = pub.Publicados.Should().ContainSingle().Which.Should().BeOfType<DiferenciaCorteAutorizadaIntegrationEvent>().Subject;
        ev.Importe.Should().Be(50m);
        ev.Tipo.Should().Be("Faltante");
        ev.CajeroId.Should().Be(Cajero);
        ev.CajaSesionId.Should().Be(sesion);
        ev.AutorizadaPor.Should().Be(Tesorero);

        var deposito = (await new DepositosHandler(db, new FakeClientes()).Handle(new DepositosQuery(), default)).Items.Single();
        deposito.Conciliado.Should().BeTrue();
        deposito.DiferenciaEstado.Should().Be(EstadoDiferenciaCorte.Autorizada);
    }

    // CA3: el cajero dueño del corte, con el permiso, intenta autorizar su diferencia → el servidor lo rechaza.
    [Fact]
    public async Task CA3_el_cajero_no_autoriza_ni_rechaza_su_propia_diferencia()
    {
        using var db = Nueva(); var pub = new FakePublisher();
        var dif = await Cerrar(db, 4_950m, -50m);

        var autorizar = () => Autorizar(db, pub, dif, Cajero);
        var rechazar = () => new RechazarDiferenciaCorteHandler(db, new FakeUser(Cajero), new FakeClock())
            .Handle(new RechazarDiferenciaCorteCommand(dif.Id, "No es mío", dif.Version), default);

        (await autorizar.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("DIF_CAJERO_MISMO_USUARIO");
        (await rechazar.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("DIF_CAJERO_MISMO_USUARIO");
        pub.Publicados.Should().BeEmpty();
        (await db.DiferenciasCorte.AsNoTracking().SingleAsync()).Estado.Should().Be(EstadoDiferenciaCorte.PendienteAutorizar);
    }

    // CA4: rechazar sin motivo no se permite; con motivo queda «Por aclarar» (y el corte sigue sin conciliar).
    [Fact]
    public async Task CA4_rechazo_exige_motivo_y_deja_por_aclarar()
    {
        using var db = Nueva();
        var dif = await Cerrar(db, 4_950m, -50m);
        var rechazar = (string motivo) => new RechazarDiferenciaCorteHandler(db, new FakeUser(Tesorero), new FakeClock())
            .Handle(new RechazarDiferenciaCorteCommand(dif.Id, motivo, dif.Version), default);

        var sinMotivo = () => rechazar(" ");
        (await sinMotivo.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("DIF_MOTIVO_VACIO");

        var r = await rechazar("Revisar el arqueo con el cajero");
        r.Estado.Should().Be(EstadoDiferenciaCorte.PorAclarar);
        r.MotivoRechazo.Should().Be("Revisar el arqueo con el cajero");
        dif.Autorizada.Should().BeFalse();
    }

    // CA5: faltante autorizado de $50; el cajero devuelve $50 («corte del 28-oct») → Saldada y evento de abono.
    [Fact]
    public async Task CA5_devolucion_total_salda_y_publica_el_abono_al_deudor()
    {
        using var db = Nueva(); var pub = new FakePublisher();
        var sesion = Guid.NewGuid();
        var dif = await Cerrar(db, 4_950m, -50m, sesion);
        await Autorizar(db, pub, dif, Tesorero);
        pub.Publicados.Clear();

        var movimiento = Guid.NewGuid();
        await Devolver(db, pub, sesion, 50m, movimiento);

        var d = await db.DiferenciasCorte.AsNoTracking().SingleAsync();
        d.Estado.Should().Be(EstadoDiferenciaCorte.Saldada);
        d.ImporteDevuelto.Should().Be(50m);
        var ev = pub.Publicados.Should().ContainSingle().Which.Should().BeOfType<DiferenciaCorteDevolucionAplicadaIntegrationEvent>().Subject;
        ev.Importe.Should().Be(50m);
        ev.Saldada.Should().BeTrue();
        ev.SaldoPendiente.Should().Be(0m);
        ev.CajeroId.Should().Be(Cajero);
        ev.DiaCorteOrigen.Should().Be(Dia);
        ev.CajaMovimientoId.Should().Be(movimiento);
    }

    [Fact]
    public async Task Devolucion_parcial_deja_saldada_parcialmente_y_no_puede_exceder_el_faltante()
    {
        using var db = Nueva(); var pub = new FakePublisher();
        var sesion = Guid.NewGuid();
        var dif = await Cerrar(db, 4_950m, -50m, sesion);
        await Autorizar(db, pub, dif, Tesorero);

        await Devolver(db, pub, sesion, 20m);
        (await db.DiferenciasCorte.AsNoTracking().SingleAsync()).Estado.Should().Be(EstadoDiferenciaCorte.SaldadaParcial);

        var exceso = () => Devolver(db, pub, sesion, 31m);
        (await exceso.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("DIF_DEVOLUCION_EXCEDE");

        await Devolver(db, pub, sesion, 30m);
        (await db.DiferenciasCorte.AsNoTracking().SingleAsync()).Estado.Should().Be(EstadoDiferenciaCorte.Saldada);
    }

    [Fact]
    public async Task Devolucion_antes_de_autorizar_se_acumula_y_el_abono_sale_al_autorizar()
    {
        using var db = Nueva(); var pub = new FakePublisher();
        var sesion = Guid.NewGuid();
        var dif = await Cerrar(db, 4_950m, -50m, sesion);

        await Devolver(db, pub, sesion, 50m);
        pub.Publicados.Should().BeEmpty();
        (await db.DiferenciasCorte.AsNoTracking().SingleAsync()).Estado.Should().Be(EstadoDiferenciaCorte.PendienteAutorizar);

        await Autorizar(db, pub, await db.DiferenciasCorte.SingleAsync(), Tesorero);

        (await db.DiferenciasCorte.AsNoTracking().SingleAsync()).Estado.Should().Be(EstadoDiferenciaCorte.Saldada);
        pub.Publicados.Select(e => e.GetType()).Should().Equal(
            typeof(DiferenciaCorteAutorizadaIntegrationEvent), typeof(DiferenciaCorteDevolucionAplicadaIntegrationEvent));
    }

    // CA6: corte con diferencia $0 → no se crea diferencia pendiente (y el corte se concilia con solo el depósito).
    [Fact]
    public async Task CA6_corte_sin_diferencia_no_crea_diferencia_y_se_concilia()
    {
        using var db = Nueva();
        var sesion = Guid.NewGuid();
        await new ProyectarExpectativaCajaHandler(db, new FakeClock())
            .Handle(new ProyectarExpectativaCajaCommand(Guid.NewGuid(), Cierre(sesion, 5_000m, 0m)), default);

        (await db.DiferenciasCorte.CountAsync()).Should().Be(0);
        var r = await ConfirmarFicha(db, 5_000m, sesion);
        r.Conciliado.Should().BeTrue();
        r.DiferenciaEstado.Should().BeNull();
    }

    [Fact]
    public async Task Evento_de_cierre_reentregado_no_duplica_la_diferencia()
    {
        using var db = Nueva();
        var sesion = Guid.NewGuid();
        var cierre = Cierre(sesion, 4_950m, -50m);
        var handler = new ProyectarExpectativaCajaHandler(db, new FakeClock());
        await handler.Handle(new ProyectarExpectativaCajaCommand(Guid.NewGuid(), cierre), default);
        await handler.Handle(new ProyectarExpectativaCajaCommand(Guid.NewGuid(), cierre), default);

        (await db.DiferenciasCorte.CountAsync()).Should().Be(1);
    }

    // Integración con #288: el sobrante forma parte del efectivo contado (la expectativa de depósito),
    // así que la ficha por el total contado cabe; una ficha mayor sigue rechazándose por exceder el corte.
    [Fact]
    public async Task Sobrante_el_deposito_es_el_contado_y_no_se_concilia_hasta_autorizar()
    {
        using var db = Nueva(); var pub = new FakePublisher();
        var sesion = Guid.NewGuid();
        var dif = await Cerrar(db, 5_050m, 50m, sesion); // esperado 5,000, contado 5,050

        dif.EsFaltante.Should().BeFalse();
        var exceso = () => ConfirmarFicha(db, 5_051m, sesion);
        (await exceso.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("DEP_FICHA_EXCEDE_CORTE");

        var r = await ConfirmarFicha(db, 5_050m, sesion);
        r.Estado.Should().Be(EstadoDepositoConfirmacion.Confirmada);
        r.Conciliado.Should().BeFalse();

        await Autorizar(db, pub, dif, Tesorero);
        var ev = pub.Publicados.Should().ContainSingle().Which.Should().BeOfType<DiferenciaCorteAutorizadaIntegrationEvent>().Subject;
        ev.Tipo.Should().Be("Sobrante");
        ev.Importe.Should().Be(50m);

        var sobrante = () => Devolver(db, pub, sesion, 10m);
        (await sobrante.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("DIF_NO_ES_FALTANTE");
    }

    private static async Task<DepositoConfirmacionResponse> ConfirmarFicha(TesoreriaDbContext db, decimal monto, Guid sesion)
    {
        var cuenta = await db.CuentasBancarias.FirstOrDefaultAsync();
        if (cuenta is null)
        {
            cuenta = new CuentaBancaria(Empresa, "BBVA", "0123456789", null, "MXN");
            db.CuentasBancarias.Add(cuenta);
        }
        var ficha = MovimientoBancario.RegistrarIngreso(Empresa, cuenta, monto, Dia, "F-1", null, null, null, Tesorero, Ahora);
        db.MovimientosBancarios.Add(ficha);
        await db.SaveChangesAsync();
        var deposito = await db.DepositosConfirmacion.SingleAsync(d => d.CajaSesionId == sesion);
        return await new ConfirmarDepositoHandler(db, new FakeEmpresa(), new FakeUser(Tesorero), new FakePublisher(), new FakeClock())
            .Handle(new ConfirmarDepositoCommand(deposito.Id, ficha.Id, deposito.Version), default);
    }

    private static Task Devolver(TesoreriaDbContext db, FakePublisher pub, Guid corteOrigen, decimal importe, Guid? movimiento = null) =>
        new AplicarDevolucionFaltanteHandler(db, pub, new FakeClock()).Handle(new AplicarDevolucionFaltanteCommand(Guid.NewGuid(),
            new CajaFaltanteDevueltoPayload(Empresa, Ahora, movimiento ?? Guid.NewGuid(), Guid.NewGuid(), corteOrigen, Cajero, importe)), default);

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => Empresa;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeUser(Guid id) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => "Tesorería diferencias";
    }

    private sealed class FakeClock : IClock { public DateTimeOffset UtcNow => Ahora; }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken) { Publicados.Add(integrationEvent); return Task.CompletedTask; }
    }

    private sealed class FakeClientes : Millet.Tesoreria.Domain.Ports.DatosMaestros.IClienteReadPort
    {
        public Task<IReadOnlyDictionary<Guid, Millet.Tesoreria.Domain.Ports.DatosMaestros.ClienteRefDto>> ObtenerVariosAsync(
            IReadOnlyCollection<Guid> clienteIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Millet.Tesoreria.Domain.Ports.DatosMaestros.ClienteRefDto>>(
                new Dictionary<Guid, Millet.Tesoreria.Domain.Ports.DatosMaestros.ClienteRefDto>());

        public Task<IReadOnlyList<Millet.Tesoreria.Domain.Ports.DatosMaestros.ClienteRefDto>> BuscarAsync(
            string texto, int limite, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Millet.Tesoreria.Domain.Ports.DatosMaestros.ClienteRefDto>>([]);
    }
}
