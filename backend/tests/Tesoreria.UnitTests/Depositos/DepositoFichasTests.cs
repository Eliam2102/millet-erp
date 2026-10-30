using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Depositos;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Depositos;

/// <summary>
/// Tarea 86e3vr1tr (Millet 23-oct): un corte de caja se cubre con 1..N fichas;
/// pendiente → parcial → confirmado; soporte desde la 2ª ficha; un evento por ficha.
/// </summary>
public sealed class DepositoFichasTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Tesorero = Guid.NewGuid();
    private static readonly Guid CajeroA = Guid.NewGuid();
    private static readonly Guid CajeroB = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly DiaCorte = new(2026, 11, 2);

    private static async Task<(TesoreriaDbContext db, CuentaBancaria cuenta)> Nueva()
    {
        var db = new TesoreriaDbContext(new DbContextOptionsBuilder<TesoreriaDbContext>()
            .UseInMemoryDatabase($"tes_fichas_{Guid.NewGuid()}").Options, new FakeEmpresa());
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "0123456789", null, "MXN");
        db.CuentasBancarias.Add(cuenta);
        await db.SaveChangesAsync();
        return (db, cuenta);
    }

    private static DepositoConfirmacion Corte(TesoreriaDbContext db, decimal efectivo, Guid cajero)
    {
        var d = DepositoConfirmacion.CrearExpectativaCaja(Empresa, Guid.NewGuid(), efectivo, DiaCorte, Sucursal, cajero);
        db.DepositosConfirmacion.Add(d);
        return d;
    }

    private static MovimientoBancario Ingreso(TesoreriaDbContext db, CuentaBancaria cuenta, decimal monto, DateOnly fecha, string? referencia = null)
    {
        var m = MovimientoBancario.RegistrarIngreso(Empresa, cuenta, monto, fecha, referencia, null, null, null, Tesorero, Ahora);
        db.MovimientosBancarios.Add(m);
        return m;
    }

    private static async Task<DepositoConfirmacionResponse> Ligar(TesoreriaDbContext db, FakePublisher pub, DepositoConfirmacion d,
        MovimientoBancario m, SoporteFicha? tipo = null, string? folio = null) =>
        await new ConfirmarDepositoHandler(db, new FakeEmpresa(), new FakeUser(), pub, new FakeClock())
            .Handle(new ConfirmarDepositoCommand(d.Id, m.Id, d.Version, null, tipo, folio), default);

    [Fact]
    public async Task CA1_una_ficha_por_el_total_confirma_y_publica_un_evento()
    {
        var (db, cuenta) = await Nueva(); var pub = new FakePublisher();
        var corte = Corte(db, 4_200m, CajeroA);
        var ficha = Ingreso(db, cuenta, 4_200m, DiaCorte, "F-100");
        await db.SaveChangesAsync();

        var r = await Ligar(db, pub, corte, ficha);

        r.Estado.Should().Be(EstadoDepositoConfirmacion.Confirmada);
        r.SaldoPorDepositar.Should().Be(0m);
        r.Fichas.Should().ContainSingle();
        var ev = pub.Publicados.Should().ContainSingle().Which.Should().BeOfType<DepositoCajaConfirmadoIntegrationEvent>().Subject;
        ev.CorteCompleto.Should().BeTrue();
        ev.CajeroId.Should().Be(CajeroA);
        ev.SucursalId.Should().Be(Sucursal);
        ev.Importe.Should().Be(4_200m);
    }

    [Fact]
    public async Task CA2_dos_fichas_con_vale_dan_parcial_luego_confirmado_y_dos_eventos_con_el_dia_del_corte()
    {
        var (db, cuenta) = await Nueva(); var pub = new FakePublisher();
        var corte = Corte(db, 4_200m, CajeroA);
        var f1 = Ingreso(db, cuenta, 2_500m, DiaCorte);
        var f2 = Ingreso(db, cuenta, 1_700m, DiaCorte.AddDays(1));
        await db.SaveChangesAsync();

        var r1 = await Ligar(db, pub, corte, f1);
        r1.Estado.Should().Be(EstadoDepositoConfirmacion.Parcial);
        r1.SaldoPorDepositar.Should().Be(1_700m);

        var r2 = await Ligar(db, pub, corte, f2, SoporteFicha.ValeCaja, "VC-0001");
        r2.Estado.Should().Be(EstadoDepositoConfirmacion.Confirmada);
        r2.Fichas!.Select(f => f.Importe).Should().Equal(2_500m, 1_700m);

        var eventos = pub.Publicados.Cast<DepositoCajaConfirmadoIntegrationEvent>().ToList();
        eventos.Should().HaveCount(2);
        eventos.Should().OnlyContain(e => e.DiaCorte == DiaCorte && e.SucursalId == Sucursal);
        eventos.Select(e => e.CorteCompleto).Should().Equal(false, true);
        eventos[1].FechaFicha.Should().Be(DiaCorte.AddDays(1));
        eventos[1].SoporteFolio.Should().Be("VC-0001");
    }

    [Fact]
    public async Task CA3_segunda_ficha_sin_soporte_se_rechaza()
    {
        var (db, cuenta) = await Nueva(); var pub = new FakePublisher();
        var corte = Corte(db, 4_200m, CajeroA);
        var f1 = Ingreso(db, cuenta, 2_500m, DiaCorte);
        var f2 = Ingreso(db, cuenta, 1_700m, DiaCorte);
        await db.SaveChangesAsync();
        await Ligar(db, pub, corte, f1);

        var sinSoporte = () => Ligar(db, pub, corte, f2);
        var sinFolio = () => Ligar(db, pub, corte, f2, SoporteFicha.CorteParcial, " ");

        (await sinSoporte.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("DEP_FICHA_SOPORTE_REQUERIDO");
        (await sinFolio.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("DEP_FICHA_SOPORTE_REQUERIDO");
        pub.Publicados.Should().HaveCount(1);
    }

    [Fact]
    public async Task CA4_ficha_ligada_al_corte_de_A_no_se_liga_al_corte_de_B()
    {
        var (db, cuenta) = await Nueva(); var pub = new FakePublisher();
        var corteA = Corte(db, 2_500m, CajeroA);
        var corteB = Corte(db, 5_000m, CajeroB);
        var ficha = Ingreso(db, cuenta, 2_500m, DiaCorte);
        await db.SaveChangesAsync();
        await Ligar(db, pub, corteA, ficha);

        var act = () => Ligar(db, pub, corteB, ficha, SoporteFicha.ValeCaja, "VC-9");

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("DEP_FICHA_YA_LIGADA");
        corteB.Fichas.Should().BeEmpty();
        pub.Publicados.Should().HaveCount(1);
    }

    [Fact]
    public async Task CA5_fichas_que_suman_mas_que_el_corte_se_rechazan()
    {
        var (db, cuenta) = await Nueva(); var pub = new FakePublisher();
        var corte = Corte(db, 4_200m, CajeroA);
        var f1 = Ingreso(db, cuenta, 2_500m, DiaCorte);
        var f2 = Ingreso(db, cuenta, 1_800m, DiaCorte);
        await db.SaveChangesAsync();
        await Ligar(db, pub, corte, f1);

        var act = () => Ligar(db, pub, corte, f2, SoporteFicha.ValeCaja, "VC-1");

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("DEP_FICHA_EXCEDE_CORTE");
        corte.Estado.Should().Be(EstadoDepositoConfirmacion.Parcial);
        f2.EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.NoAplicado);
    }

    [Fact]
    public async Task Corte_confirmado_ya_no_admite_mas_fichas()
    {
        var (db, cuenta) = await Nueva(); var pub = new FakePublisher();
        var corte = Corte(db, 1_000m, CajeroA);
        var f1 = Ingreso(db, cuenta, 1_000m, DiaCorte);
        var f2 = Ingreso(db, cuenta, 10m, DiaCorte);
        await db.SaveChangesAsync();
        await Ligar(db, pub, corte, f1);

        var act = () => Ligar(db, pub, corte, f2, SoporteFicha.ValeCaja, "VC-2");

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("DEP_YA_RESUELTA");
    }

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => Empresa;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeUser : ICurrentUserContext
    {
        public Guid? UserId => Tesorero;
        public string? UserName => "Tesorería fichas";
    }

    private sealed class FakeClock : IClock { public DateTimeOffset UtcNow => Ahora; }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct) { Publicados.Add(integrationEvent); return Task.CompletedTask; }
    }
}
