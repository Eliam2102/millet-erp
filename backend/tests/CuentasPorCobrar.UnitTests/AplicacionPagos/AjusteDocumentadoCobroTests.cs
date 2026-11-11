using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.AplicacionPagos;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Adapters;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.AplicacionPagos;

/// <summary>
/// CXC-06 (86e3qddef): solo un caso autorizado se trata como ajuste documentado (importe aplicado, ajuste, cuenta y motivo); no se
/// absorbe cualquier faltante y la misma persona no autoriza su propio ajuste.
/// </summary>
public sealed class AjusteDocumentadoCobroTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid Proponente = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Autorizador = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static PropuestaAplicacionPago Crear(decimal deposito, TipoAjusteCobro? tipo, string? motivo) =>
        PropuestaAplicacionPago.Crear(EmpresaId, ClienteId, "DEP-1", deposito, "USD", "REM-1",
            [("uuid-1", Guid.NewGuid(), 10_000m, null)], 50m, Proponente, tipoAjuste: tipo, motivoAjuste: motivo);

    [Fact]
    public void Faltante_sin_tipo_no_se_absorbe()
    {
        var act = () => Crear(9_952m, null, "Comisión");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_AJUSTE_SIN_TIPO");
    }

    [Fact]
    public void Faltante_sin_motivo_no_se_absorbe()
    {
        var act = () => Crear(9_952m, TipoAjusteCobro.ComisionBancaria, "  ");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_AJUSTE_SIN_MOTIVO");
    }

    [Fact]
    public void Faltante_documentado_queda_por_autorizar_con_importe_cuenta_y_motivo()
    {
        var p = Crear(9_952m, TipoAjusteCobro.ComisionBancaria, " Comisión del banco ");
        p.AjusteNoFiscal.Should().Be(-48m);
        p.TipoAjuste.Should().Be(TipoAjusteCobro.ComisionBancaria);
        p.MotivoAjuste.Should().Be("Comisión del banco");
        p.AjustePorAutorizar.Should().BeTrue();
    }

    [Fact]
    public void Deposito_exacto_no_pide_ajuste_ni_autorizacion()
    {
        var p = Crear(10_000m, TipoAjusteCobro.ComisionBancaria, "ignorado");
        p.TipoAjuste.Should().BeNull();
        p.MotivoAjuste.Should().BeNull();
        p.AjustePorAutorizar.Should().BeFalse();
    }

    [Fact]
    public void Quien_propone_no_autoriza_su_propio_ajuste()
    {
        var p = Crear(9_952m, TipoAjusteCobro.ComisionBancaria, "Comisión");
        var act = () => p.AutorizarAjuste(Proponente, Ahora);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_AJUSTE_MISMO_USUARIO");
        p.AjustePorAutorizar.Should().BeTrue();
    }

    [Fact]
    public void Otra_persona_autoriza_el_ajuste_una_sola_vez()
    {
        var p = Crear(9_952m, TipoAjusteCobro.DiferenciaMenor, "Redondeo del banco");
        p.AutorizarAjuste(Autorizador, Ahora, tieneSoporte: true);
        p.AjusteAutorizadoPor.Should().Be(Autorizador);
        p.AjusteAutorizadoEn.Should().Be(Ahora);
        p.AjustePorAutorizar.Should().BeFalse();
        var otraVez = () => p.AutorizarAjuste(Guid.NewGuid(), Ahora, tieneSoporte: true);
        otraVez.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_AJUSTE_YA_AUTORIZADO");
    }

    [Fact]
    public async Task Con_ajuste_la_propuesta_no_llega_a_Tesoreria_hasta_que_otra_persona_la_autoriza()
    {
        using var db = CrearDbContext();
        AgregarFactura(db, "uuid-a", 10_000m);
        await db.SaveChangesAsync();
        var publisher = new FakePublisher();

        var creada = await new CrearPropuestaAplicacionHandler(db, publisher, new FakeEmpresaContext(EmpresaId),
                Options.Create(new AplicacionPagosOptions()), new FakeClock(), new FakeUser(Proponente))
            .Handle(new CrearPropuestaAplicacionCommand(ClienteId, "DEP-9", 9_995m, "MXN", "REM-9",
                [new PropuestaFacturaLinea("uuid-a", 10_000m, 1)], TipoAjuste: TipoAjusteCobro.DiferenciaMenor,
                MotivoAjuste: "Diferencia del banco"), default);

        creada.AjustePorAutorizar.Should().BeTrue();
        publisher.Publicados.Should().BeEmpty("Tesorería no debe ver una propuesta con ajuste sin autorizar");

        var mismo = () => new AutorizarAjustePropuestaHandler(db, publisher, new FakeUser(Proponente), new FakeClock())
            .Handle(new AutorizarAjustePropuestaCommand(creada.Id, creada.Version), default);
        await mismo.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAP_AJUSTE_MISMO_USUARIO");
        publisher.Publicados.Should().BeEmpty();

        var autorizada = await new AutorizarAjustePropuestaHandler(db, publisher, new FakeUser(Autorizador), new FakeClock(), new SoporteFijo(true))
            .Handle(new AutorizarAjustePropuestaCommand(creada.Id, creada.Version), default);

        autorizada.AjustePorAutorizar.Should().BeFalse();
        autorizada.AjusteAutorizadoPor.Should().Be(Autorizador);
        var evento = publisher.Publicados.Should().ContainSingle().Which.Should().BeOfType<PropuestaAplicacionPagoCreadaEvent>().Subject;
        evento.AjusteNoFiscal.Should().Be(-5m);
        evento.Facturas.Single().ImporteAplicado.Should().Be(10_000m);
    }

    [Fact]
    public async Task Rechazar_el_ajuste_rechaza_la_propuesta_con_motivo()
    {
        using var db = CrearDbContext();
        var p = Crear(9_952m, TipoAjusteCobro.ComisionBancaria, "Comisión");
        db.PropuestasAplicacionPago.Add(p);
        await db.SaveChangesAsync();

        var r = await new RechazarAjustePropuestaHandler(db, new FakeUser(Autorizador), new FakeClock())
            .Handle(new RechazarAjustePropuestaCommand(p.Id, p.Version, "No es comisión"), default);

        r.Estado.Should().Be(EstadoPropuestaAplicacion.Rechazada);
        r.MotivoRechazo.Should().Be("Ajuste rechazado: No es comisión");
    }

    [Fact]
    public async Task Contabilidad_solo_recibe_el_ajuste_autorizado()
    {
        using var db = CrearDbContext();
        var p = Crear(9_952m, TipoAjusteCobro.ComisionBancaria, "Comisión del banco");
        db.PropuestasAplicacionPago.Add(p);
        await db.SaveChangesAsync();
        var puerto = new AjusteCobroContableAdapter(db);

        (await puerto.ObtenerAsync(EmpresaId, p.Id, default)).Should().BeNull("sin autorización no hay ajuste contable");

        p.AutorizarAjuste(Autorizador, Ahora, tieneSoporte: true);
        await db.SaveChangesAsync();
        var ajuste = await puerto.ObtenerAsync(EmpresaId, p.Id, default);

        ajuste.Should().NotBeNull();
        ajuste!.Tipo.Should().Be(TipoAjusteCobroContable.ComisionBancaria);
        ajuste.Importe.Should().Be(48m);
        ajuste.Motivo.Should().Be("Comisión del banco");
        ajuste.AutorizadoPor.Should().Be(Autorizador);
        (await puerto.ObtenerAsync(Guid.NewGuid(), p.Id, default)).Should().BeNull("otra empresa no ve el ajuste");
    }

    // --------------------------------------------------- helpers

    private static void AgregarFactura(CuentasPorCobrarDbContext db, string uuid, decimal total) =>
        db.FacturasCartera.Add(FacturaCartera.Crear(EmpresaId, Guid.NewGuid(), ClienteId, "VGL860910IU4", "Cliente",
            uuid, "FV-1", total, "MXN", "PPD", Ahora.AddDays(-30), Ahora.AddDays(15)));

    private static CuentasPorCobrarDbContext CrearDbContext() =>
        new(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase($"cxc_ajuste_{Guid.NewGuid()}").Options, new FakeEmpresaContext(EmpresaId));

    private sealed class FakeUser(Guid id) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => "Usuario de prueba";
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

    private sealed class FakeEmpresaContext(Guid empresaId) : ICurrentEmpresaContext
    {
        public Guid? Current => empresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
