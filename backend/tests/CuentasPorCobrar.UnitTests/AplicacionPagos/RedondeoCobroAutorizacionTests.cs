using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.AplicacionPagos;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.AplicacionPagos;

/// <summary>
/// M-326 parte 2 (c): todo redondeo de cobro requiere la autorización del Gerente de Área (permiso propio), de otra persona distinta
/// de quien propone, y Tesorería no ve la propuesta hasta entonces. Los ajustes documentados siguen con el permiso de confirmar.
/// </summary>
public sealed class RedondeoCobroAutorizacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid Proponente = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Gerente = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    [Fact]
    public async Task El_redondeo_no_llega_a_Tesoreria_hasta_que_el_gerente_lo_autoriza()
    {
        using var db = CrearDbContext();
        var publisher = new FakePublisher();
        var creada = await CrearRedondeoAsync(db, publisher);

        creada.AjustePorAutorizar.Should().BeTrue();
        publisher.Publicados.Should().BeEmpty("Tesorería no debe ver el cobro con redondeo sin autorizar");

        var autorizada = await new AutorizarAjustePropuestaHandler(db, publisher, new FakeUser(Gerente), new FakeClock(), null, new Permisos(true))
            .Handle(new AutorizarAjustePropuestaCommand(creada.Id, creada.Version), default);

        autorizada.AjustePorAutorizar.Should().BeFalse();
        autorizada.AjusteAutorizadoPor.Should().Be(Gerente);
        publisher.Publicados.Should().ContainSingle().Which.Should().BeOfType<PropuestaAplicacionPagoCreadaEvent>();
    }

    [Fact]
    public async Task Sin_el_permiso_de_gerente_no_se_autoriza_ni_se_rechaza_un_redondeo()
    {
        using var db = CrearDbContext();
        var publisher = new FakePublisher();
        var creada = await CrearRedondeoAsync(db, publisher);

        var autorizar = () => new AutorizarAjustePropuestaHandler(db, publisher, new FakeUser(Gerente), new FakeClock(), null, new Permisos(false))
            .Handle(new AutorizarAjustePropuestaCommand(creada.Id, creada.Version), default);
        await autorizar.Should().ThrowAsync<ForbiddenException>().Where(e => e.Code == "PAP_REDONDEO_REQUIERE_GERENTE");

        var sinPuerto = () => new AutorizarAjustePropuestaHandler(db, publisher, new FakeUser(Gerente), new FakeClock())
            .Handle(new AutorizarAjustePropuestaCommand(creada.Id, creada.Version), default);
        await sinPuerto.Should().ThrowAsync<ForbiddenException>("sin el puerto de permisos falla cerrado");

        var rechazar = () => new RechazarAjustePropuestaHandler(db, new FakeUser(Gerente), new FakeClock(), new Permisos(false))
            .Handle(new RechazarAjustePropuestaCommand(creada.Id, creada.Version, "No procede"), default);
        await rechazar.Should().ThrowAsync<ForbiddenException>().Where(e => e.Code == "PAP_REDONDEO_REQUIERE_GERENTE");
        publisher.Publicados.Should().BeEmpty();
    }

    [Fact]
    public async Task Quien_propone_no_autoriza_su_propio_redondeo_aunque_sea_gerente()
    {
        using var db = CrearDbContext();
        var publisher = new FakePublisher();
        var creada = await CrearRedondeoAsync(db, publisher);

        var mismo = () => new AutorizarAjustePropuestaHandler(db, publisher, new FakeUser(Proponente), new FakeClock(), null, new Permisos(true))
            .Handle(new AutorizarAjustePropuestaCommand(creada.Id, creada.Version), default);
        await mismo.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAP_AJUSTE_MISMO_USUARIO");
    }

    [Fact]
    public async Task El_gerente_puede_rechazar_el_redondeo()
    {
        using var db = CrearDbContext();
        var creada = await CrearRedondeoAsync(db, new FakePublisher());

        var r = await new RechazarAjustePropuestaHandler(db, new FakeUser(Gerente), new FakeClock(), new Permisos(true))
            .Handle(new RechazarAjustePropuestaCommand(creada.Id, creada.Version, "No procede"), default);

        r.Estado.Should().Be(EstadoPropuestaAplicacion.Rechazada);
    }

    [Fact]
    public async Task Un_ajuste_documentado_no_exige_el_permiso_de_gerente()
    {
        using var db = CrearDbContext();
        var publisher = new FakePublisher();
        AgregarFactura(db);
        await db.SaveChangesAsync();
        var creada = await new CrearPropuestaAplicacionHandler(db, publisher, new FakeEmpresaContext(EmpresaId),
                Options.Create(new AplicacionPagosOptions()), new FakeClock(), new FakeUser(Proponente))
            .Handle(new CrearPropuestaAplicacionCommand(ClienteId, "DEP-2", 9_995m, "MXN", "REM-2",
                [new PropuestaFacturaLinea("uuid-a", 10_000m, 1)], TipoAjuste: TipoAjusteCobro.DiferenciaMenor,
                MotivoAjuste: "Diferencia del banco"), default);

        var autorizada = await new AutorizarAjustePropuestaHandler(db, publisher, new FakeUser(Gerente), new FakeClock(), null, new Permisos(false))
            .Handle(new AutorizarAjustePropuestaCommand(creada.Id, creada.Version), default);

        autorizada.AjustePorAutorizar.Should().BeFalse();
    }

    [Fact]
    public void El_permiso_de_gerente_esta_en_el_catalogo_canonico()
    {
        PermisosCanonicos.CuentasPorCobrarAplicacionPagoAutorizarRedondeo.Should().Be("cuentas_por_cobrar.aplicacion-pago.autorizar-redondeo");
        PermisosCanonicos.Todos.Should().Contain(p => p.Codigo == PermisosCanonicos.CuentasPorCobrarAplicacionPagoAutorizarRedondeo);
    }

    // --------------------------------------------------- helpers

    private static async Task<PropuestaAplicacionResponse> CrearRedondeoAsync(CuentasPorCobrarDbContext db, FakePublisher publisher)
    {
        AgregarFactura(db);
        await db.SaveChangesAsync();
        return await new CrearPropuestaAplicacionHandler(db, publisher, new FakeEmpresaContext(EmpresaId),
                Options.Create(new AplicacionPagosOptions()), new FakeClock(), new FakeUser(Proponente))
            .Handle(new CrearPropuestaAplicacionCommand(ClienteId, "DEP-1", 9_999.60m, "MXN", "REM-1",
                [new PropuestaFacturaLinea("uuid-a", 10_000m, 1)]), default);
    }

    private static void AgregarFactura(CuentasPorCobrarDbContext db) =>
        db.FacturasCartera.Add(FacturaCartera.Crear(EmpresaId, Guid.NewGuid(), ClienteId, "VGL860910IU4", "Cliente",
            "uuid-a", "FV-1", 10_000m, "MXN", "PPD", Ahora.AddDays(-30), Ahora.AddDays(15)));

    private static CuentasPorCobrarDbContext CrearDbContext() =>
        new(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase($"cxc_redondeo_aut_{Guid.NewGuid()}").Options, new FakeEmpresaContext(EmpresaId));

    private sealed class Permisos(bool otorga) : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(otorga && permiso == PermisosCanonicos.CuentasPorCobrarAplicacionPagoAutorizarRedondeo);
    }

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
