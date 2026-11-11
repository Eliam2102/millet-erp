using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.CuentasPorCobrar.Application.AplicacionPagos;
using Millet.CuentasPorCobrar.Application.ConciliacionPolizas;
using Millet.CuentasPorCobrar.Application.EventListeners;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Ports.Adjuntos;
using Millet.CuentasPorCobrar.Infrastructure.Adapters;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.AplicacionPagos;

/// <summary>
/// M-240 (86e4083m7): la comisión bancaria solo se autoriza con su soporte adjunto; un ajuste autorizado y ya aplicado se revierte con
/// motivo, el faltante regresa a la factura y, con la póliza contraria, la conciliación de cobros contra pólizas sigue cuadrando.
/// </summary>
public sealed class SoporteYReversaAjusteCobroTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoy = new(2026, 11, 11);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid Proponente = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Autorizador = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Tesoreria = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    // ------------------------------------------------------------------ soporte (s02)

    [Fact]
    public void Comision_sin_soporte_no_se_autoriza()
    {
        var p = Propuesta(TipoAjusteCobro.ComisionBancaria);
        var act = () => p.AutorizarAjuste(Autorizador, Ahora, tieneSoporte: false);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_AJUSTE_SIN_SOPORTE");
        p.AjustePorAutorizar.Should().BeTrue();
    }

    [Fact]
    public void Diferencia_menor_no_exige_soporte()
    {
        var p = Propuesta(TipoAjusteCobro.DiferenciaMenor);
        p.AutorizarAjuste(Autorizador, Ahora);
        p.AjusteAutorizadoPor.Should().Be(Autorizador);
    }

    [Fact]
    public async Task El_handler_consulta_el_soporte_adjunto_antes_de_autorizar_la_comision()
    {
        using var db = CrearDbContext();
        var factura = FacturaCartera.Crear(EmpresaId, Guid.NewGuid(), ClienteId, "VGL860910IU4", "Cliente",
            "uuid-1", "FV-1", 10_000m, "MXN", "PPD", Ahora.AddDays(-30), Ahora.AddDays(15));
        db.FacturasCartera.Add(factura);
        var p = Propuesta(TipoAjusteCobro.ComisionBancaria, factura.Id);
        db.PropuestasAplicacionPago.Add(p);
        await db.SaveChangesAsync();

        var sinSoporte = () => new AutorizarAjustePropuestaHandler(db, new NullPublisher(), new FakeUser(Autorizador), new FakeClock(), new SoporteFijo(false))
            .Handle(new AutorizarAjustePropuestaCommand(p.Id, p.Version), default);
        await sinSoporte.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAP_AJUSTE_SIN_SOPORTE");

        var sinPuerto = () => new AutorizarAjustePropuestaHandler(db, new NullPublisher(), new FakeUser(Autorizador), new FakeClock())
            .Handle(new AutorizarAjustePropuestaCommand(p.Id, p.Version), default);
        await sinPuerto.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAP_AJUSTE_SIN_SOPORTE", "sin puerto falla cerrado");

        var soporte = new SoporteFijo(true);
        var r = await new AutorizarAjustePropuestaHandler(db, new NullPublisher(), new FakeUser(Autorizador), new FakeClock(), soporte)
            .Handle(new AutorizarAjustePropuestaCommand(p.Id, p.Version), default);
        r.AjusteAutorizadoPor.Should().Be(Autorizador);
        soporte.Consultada.Should().Be(p.Id);
    }

    // ------------------------------------------------------------------ reversa (s06)

    [Fact]
    public async Task Revertir_regresa_el_faltante_a_la_factura_y_registra_la_poliza_contraria()
    {
        using var db = CrearDbContext();
        var (p, factura, rep) = await CobroAplicadoAsync(db);
        factura.SaldoPendiente.Should().Be(0m, "el REP dio la factura por pagada con el ajuste");
        var contabilidad = new ContabilidadFija(db);

        var r = await new RevertirAjustePropuestaHandler(db, new FakeUser(Autorizador), new FakeClock(), contabilidad)
            .Handle(new RevertirAjustePropuestaCommand(p.Id, p.Version, "  El banco devolvió la comisión  "), default);

        factura.SaldoPendiente.Should().Be(48m, "el faltante regresa a la factura");
        r.AjusteRevertidoPor.Should().Be(Autorizador);
        r.MotivoReversaAjuste.Should().Be("El banco devolvió la comisión");
        r.PolizaReversaAjusteId.Should().Be(contabilidad.PolizaId);
        var mov = await db.MovimientosCartera.SingleAsync(m => m.Tipo == TipoMovimientoCartera.ReversaAjusteCobro);
        mov.Importe.Should().Be(48m);
        mov.OrigenComprobanteId.Should().Be(rep);
        contabilidad.Reversa.Should().NotBeNull("Contabilidad lee la reversa por el puerto del ajuste");
        contabilidad.Reversa!.Importe.Should().Be(48m);
        contabilidad.Reversa.Tipo.Should().Be(TipoAjusteCobroContable.ComisionBancaria);
        contabilidad.Reversa.MovimientoBancarioId.Should().Be(p.MovimientoBancarioId!.Value);
        contabilidad.Reversa.ClienteId.Should().Be(ClienteId);
    }

    [Fact]
    public async Task Reversa_y_conciliacion_de_cobros_contra_polizas_cuadran()
    {
        using var db = CrearDbContext();
        var (p, _, _) = await CobroAplicadoAsync(db);
        await new RevertirAjustePropuestaHandler(db, new FakeUser(Autorizador), new FakeClock(), new ContabilidadFija(db))
            .Handle(new RevertirAjustePropuestaCommand(p.Id, p.Version, "Comisión devuelta"), default);

        // Póliza del pago (abona 10,000 a clientes: depósito + comisión) y su contraria (carga 48 al cliente), mismo documento.
        var movimiento = p.MovimientoBancarioId!.Value;
        var polizas = new CobrosFijos(
            new PolizaCobroLectura(Guid.NewGuid(), "IG-000001", Hoy, DocumentoCobroContable.PagoCliente, movimiento, ClienteId, "MXN", 10_000m),
            new PolizaCobroLectura(Guid.NewGuid(), "IG-000002", Hoy, DocumentoCobroContable.PagoCliente, movimiento, ClienteId, "MXN", -48m));

        var r = await new ConciliacionCobrosPolizasHandler(db, polizas).Handle(new ConciliacionCobrosPolizasQuery(Hoy, Hoy), default);

        var fila = r.Filas.Should().ContainSingle().Subject;
        fila.ImporteCartera.Should().Be(9_952m);
        fila.ImportePolizas.Should().Be(9_952m);
        fila.Estado.Should().Be(EstadoConciliacionCobro.Cuadra);
    }

    [Fact]
    public async Task Si_Contabilidad_no_registra_la_contraria_la_reversa_falla()
    {
        using var db = CrearDbContext();
        var (p, _, _) = await CobroAplicadoAsync(db);

        var act = () => new RevertirAjustePropuestaHandler(db, new FakeUser(Autorizador), new FakeClock(), new ContabilidadQueFalla())
            .Handle(new RevertirAjustePropuestaCommand(p.Id, p.Version, "Comisión devuelta"), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CONTAB_PERIODO_CERRADO");
        // InMemory no tiene transacción: el rollback real lo cubre la BD; aquí se valida que se intentó registrar antes de confirmar.
        p.PolizaReversaAjusteId.Should().BeNull();
    }

    [Fact]
    public async Task Cancelar_el_REP_despues_de_revertir_deja_la_factura_con_su_saldo_original()
    {
        using var db = CrearDbContext();
        var (p, factura, rep) = await CobroAplicadoAsync(db);
        await new RevertirAjustePropuestaHandler(db, new FakeUser(Autorizador), new FakeClock(), new ContabilidadFija(db))
            .Handle(new RevertirAjustePropuestaCommand(p.Id, p.Version, "Comisión devuelta"), default);

        await new ComprobanteCanceladoHandler(db, new FakeClock(), NullLogger<ComprobanteCanceladoHandler>.Instance)
            .Handle(new ComprobanteCanceladoCommand(Guid.NewGuid(),
                new ComprobanteCanceladoPayload(EmpresaId, Ahora, rep, "Pago", "uuid-rep")), default);

        factura.SaldoPendiente.Should().Be(10_000m);
        (await db.MovimientosCartera.CountAsync(m => !m.Revertido)).Should().Be(0);
    }

    [Fact]
    public void Solo_se_revierte_un_ajuste_autorizado_de_un_cobro_aplicado_y_no_por_quien_lo_propuso()
    {
        var pendiente = Propuesta(TipoAjusteCobro.ComisionBancaria);
        pendiente.AutorizarAjuste(Autorizador, Ahora, tieneSoporte: true);
        var sinConfirmar = () => pendiente.RevertirAjuste(Autorizador, "x", Ahora);
        sinConfirmar.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_AJUSTE_NO_APLICADO");

        var p = Propuesta(TipoAjusteCobro.ComisionBancaria);
        p.AutorizarAjuste(Autorizador, Ahora, tieneSoporte: true);
        p.Confirmar(Tesoreria, Ahora, Guid.NewGuid());
        p.MarcarReppTimbrado(Guid.NewGuid());

        var mismo = () => p.RevertirAjuste(Proponente, "x", Ahora);
        mismo.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_REVERSA_MISMO_USUARIO");
        var sinMotivo = () => p.RevertirAjuste(Autorizador, " ", Ahora);
        sinMotivo.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_MOTIVO_REVERSA_VACIO");

        p.RevertirAjuste(Autorizador, "Comisión devuelta", Ahora);
        p.AjusteRevertido.Should().BeTrue();
        var otraVez = () => p.RevertirAjuste(Autorizador, "x", Ahora);
        otraVez.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_AJUSTE_YA_REVERTIDO");

        var sinAjuste = PropuestaAplicacionPago.Crear(EmpresaId, ClienteId, "DEP-2", 10_000m, "MXN", "REM-2",
            [("uuid-2", Guid.NewGuid(), 10_000m, null)], 50m, Proponente);
        var nada = () => sinAjuste.RevertirAjuste(Autorizador, "x", Ahora);
        nada.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_SIN_AJUSTE_AUTORIZADO");
    }

    // ------------------------------------------------------------------ helpers

    private static PropuestaAplicacionPago Propuesta(TipoAjusteCobro tipo, Guid? facturaId = null) =>
        PropuestaAplicacionPago.Crear(EmpresaId, ClienteId, "DEP-1", 9_952m, "MXN", "REM-1",
            [("uuid-1", facturaId ?? Guid.NewGuid(), 10_000m, 1)], 50m, Proponente, tipoAjuste: tipo, motivoAjuste: "Comisión del banco");

    /// <summary>Cobro corto con comisión autorizada, confirmado por Tesorería y con REP timbrado aplicado en cartera.</summary>
    private static async Task<(PropuestaAplicacionPago, FacturaCartera, Guid Rep)> CobroAplicadoAsync(CuentasPorCobrarDbContext db)
    {
        var factura = FacturaCartera.Crear(EmpresaId, Guid.NewGuid(), ClienteId, "VGL860910IU4", "Cliente",
            "uuid-1", "FV-1", 10_000m, "MXN", "PPD", Ahora.AddDays(-30), Ahora.AddDays(15));
        db.FacturasCartera.Add(factura);
        var p = Propuesta(TipoAjusteCobro.ComisionBancaria, factura.Id);
        p.AutorizarAjuste(Autorizador, Ahora, tieneSoporte: true);
        p.Confirmar(Tesoreria, Ahora, Guid.NewGuid());
        var rep = Guid.NewGuid();
        p.MarcarReppTimbrado(rep);
        db.PropuestasAplicacionPago.Add(p);
        factura.AplicarPago(10_000m);
        db.MovimientosCartera.Add(new MovimientoCartera(EmpresaId, factura.Id, TipoMovimientoCartera.Pago, rep, 10_000m, Ahora));
        await db.SaveChangesAsync();
        return (p, factura, rep);
    }

    private static CuentasPorCobrarDbContext CrearDbContext() =>
        new(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase($"cxc_m240_{Guid.NewGuid()}").Options, new FakeEmpresaContext(EmpresaId));

    /// <summary>Contabilidad de prueba: lee la reversa por el adaptador real de CxC (mismo camino) y devuelve una póliza.</summary>
    private sealed class ContabilidadFija(CuentasPorCobrarDbContext db) : IReversaAjusteCobroContablePort
    {
        public Guid PolizaId { get; } = Guid.NewGuid();
        public ReversaAjusteCobroContable? Reversa { get; private set; }

        public async Task<PolizaRegistrada> RegistrarAsync(Guid empresaId, Guid propuestaId, CancellationToken ct)
        {
            Reversa = await new AjusteCobroContableAdapter(db).ObtenerReversaAsync(empresaId, propuestaId, ct);
            return new PolizaRegistrada(PolizaId, "IG-000002", EstadoPoliza.Autorizada, false);
        }
    }

    private sealed class ContabilidadQueFalla : IReversaAjusteCobroContablePort
    {
        public Task<PolizaRegistrada> RegistrarAsync(Guid empresaId, Guid propuestaId, CancellationToken ct) =>
            throw new BusinessRuleException("CONTAB_PERIODO_CERRADO", "El periodo está cerrado.");
    }

    private sealed class CobrosFijos(params PolizaCobroLectura[] polizas) : ICobrosContabilizadosReadPort
    {
        public Task<IReadOnlyList<PolizaCobroLectura>> ListarAsync(DateOnly desde, DateOnly hasta, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PolizaCobroLectura>>(polizas);

        public Task<IReadOnlyList<PolizaCobroLectura>> ListarPorDocumentosAsync(string tipoDocumento, IReadOnlyCollection<Guid> documentos,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PolizaCobroLectura>>(polizas.Where(p => p.TipoDocumento == tipoDocumento && documentos.Contains(p.DocumentoId)).ToList());

        public Task<IReadOnlyList<EfectivoCobroLectura>> EfectivoAsync(IReadOnlyCollection<Guid> cobrosMostrador, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<EfectivoCobroLectura>>([]);
    }

    private sealed class FakeUser(Guid id) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => "Usuario de prueba";
    }

    private sealed class NullPublisher : IIntegrationEventPublisher
    {
        public Task PublishAsync(object integrationEvent, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class FakeEmpresaContext(Guid empresaId) : ICurrentEmpresaContext
    {
        public Guid? Current => empresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}

/// <summary>M-240: puerto de soporte de prueba (¿hay adjunto vigente?).</summary>
internal sealed class SoporteFijo(bool tiene) : ISoporteAjusteCobroReadPort
{
    public Guid? Consultada { get; private set; }

    public Task<bool> TieneSoporteAsync(Guid propuestaId, CancellationToken ct)
    {
        Consultada = propuestaId;
        return Task.FromResult(tiene);
    }
}
