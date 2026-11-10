using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Movimientos;
using Millet.Tesoreria.Application.Reportes;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Movimientos;

/// <summary>M-203 (86e3zr7k1): traspaso entre cuentas propias, par ligado, fuera del flujo y con reversa doble.</summary>
public sealed class TraspasoTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Fecha = new(2026, 10, 30);

    private static CuentaBancaria Cuenta(string banco, string numero, string moneda = "MXN") => new(Empresa, banco, numero, null, moneda);

    [Fact]
    public void Caso1_crea_egreso_en_origen_e_ingreso_en_destino_ligados_con_la_misma_referencia()
    {
        var bbva = Cuenta("BBVA", "00000004468"); var santander = Cuenta("Santander", "00000008138");
        var (egreso, ingreso) = MovimientoBancario.RegistrarTraspaso(bbva, santander, 50_000m, Fecha, "trasp-001", "Fondeo", Usuario, Ahora);

        egreso.Sentido.Should().Be(SentidoMovimiento.Egreso); egreso.CuentaBancariaId.Should().Be(bbva.Id);
        ingreso.Sentido.Should().Be(SentidoMovimiento.Ingreso); ingreso.CuentaBancariaId.Should().Be(santander.Id);
        egreso.Monto.Should().Be(50_000m); ingreso.Monto.Should().Be(50_000m);
        egreso.ReferenciaBancaria.Should().Be("TRASP-001"); ingreso.ReferenciaBancaria.Should().Be("TRASP-001");
        egreso.TraspasoId.Should().NotBeNull().And.Be(ingreso.TraspasoId);
        egreso.ConceptoId.Should().Be(ConceptoMovimiento.TraspasoId); ingreso.ConceptoId.Should().Be(ConceptoMovimiento.TraspasoId);
    }

    [Fact]
    public void Caso4_origen_igual_a_destino_o_monedas_distintas_fallan_con_error_claro()
    {
        var a = Cuenta("BBVA", "00000004468"); var usd = Cuenta("Santander", "00000008138", "USD");
        var mismaCuenta = () => MovimientoBancario.RegistrarTraspaso(a, a, 1m, Fecha, "R", null, Usuario, Ahora);
        mismaCuenta.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("TRASPASO_MISMA_CUENTA");
        var otraMoneda = () => MovimientoBancario.RegistrarTraspaso(a, usd, 1m, Fecha, "R", null, Usuario, Ahora);
        otraMoneda.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("TRASPASO_MONEDA_DISTINTA");
    }

    [Fact]
    public void Rechaza_monto_cero_referencia_vacia_y_cuenta_inactiva()
    {
        var a = Cuenta("BBVA", "00000004468"); var b = Cuenta("Santander", "00000008138");
        ((Action)(() => MovimientoBancario.RegistrarTraspaso(a, b, 0m, Fecha, "R", null, Usuario, Ahora)))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("MOV_MONTO_INVALIDO");
        ((Action)(() => MovimientoBancario.RegistrarTraspaso(a, b, 1m, Fecha, "  ", null, Usuario, Ahora)))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("TRASPASO_REFERENCIA_VACIA");
        b.Desactivar();
        ((Action)(() => MovimientoBancario.RegistrarTraspaso(a, b, 1m, Fecha, "R", null, Usuario, Ahora)))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("MOV_CUENTA_INACTIVA");
    }

    private sealed class Empresa_ : ICurrentEmpresaContext
    {
        public Guid? Current => Empresa; public bool IsBypassed => true;
        public IDisposable Bypass() => new Nada();
        private sealed class Nada : IDisposable { public void Dispose() { } }
    }
    private sealed class Usuario_ : ICurrentUserContext
    {
        public Guid? UserId => Usuario; public string? UserName => "test";
    }
    private sealed class Periodo : IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true);
    }
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => Ahora; }

    private static TesoreriaDbContext Db()
    {
        var db = new TesoreriaDbContext(new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa_());
        db.Database.EnsureCreated(); // siembra el catálogo de conceptos (HasData)
        return db;
    }

    [Fact]
    public async Task Caso3_revertir_un_lado_revierte_ambos_y_el_saldo_vuelve_al_anterior_y_no_se_revierte_dos_veces()
    {
        using var db = Db();
        var a = Cuenta("BBVA", "00000004468"); var b = Cuenta("Santander", "00000008138");
        db.CuentasBancarias.AddRange(a, b);
        await db.SaveChangesAsync();
        var r = await new RegistrarTraspasoHandler(db, new Empresa_(), new Usuario_(), new Periodo(), new Reloj())
            .Handle(new RegistrarTraspasoCommand(a.Id, b.Id, 50_000m, Fecha, "TRASP-001", "Fondeo"), default);

        var contras = await new RevertirTraspasoHandler(db, new Usuario_(), new Periodo(), new Reloj())
            .Handle(new RevertirTraspasoCommand(r.Egreso.Id, "captura duplicada"), default);

        contras.Should().HaveCount(2);
        contras.Select(c => c.CuentaBancariaId).Should().BeEquivalentTo([a.Id, b.Id]);
        contras.Should().OnlyContain(c => c.MotivoReversa == "captura duplicada");
        foreach (var cuenta in new[] { a, b })
            (await db.MovimientosBancarios.Where(m => m.CuentaBancariaId == cuenta.Id)
                .SumAsync(m => m.Sentido == SentidoMovimiento.Ingreso ? m.Monto : -m.Monto)).Should().Be(0m);

        var otra = async () => await new RevertirTraspasoHandler(db, new Usuario_(), new Periodo(), new Reloj())
            .Handle(new RevertirTraspasoCommand(r.Ingreso.Id, "otra vez"), default);
        (await otra.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("TRASPASO_YA_REVERTIDO");
    }

    [Fact]
    public async Task Caso2_el_flujo_de_efectivo_no_cuenta_el_traspaso_pero_los_saldos_si_se_mueven()
    {
        using var db = Db();
        var a = Cuenta("BBVA", "00000004468"); var b = Cuenta("Santander", "00000008138");
        db.CuentasBancarias.AddRange(a, b);
        db.MovimientosBancarios.Add(MovimientoBancario.RegistrarIngreso(Empresa, a, 100_000m, new DateOnly(2026, 10, 5), null, null, null, null, Usuario, Ahora));
        await db.SaveChangesAsync();
        await new RegistrarTraspasoHandler(db, new Empresa_(), new Usuario_(), new Periodo(), new Reloj())
            .Handle(new RegistrarTraspasoCommand(a.Id, b.Id, 50_000m, Fecha, "TRASP-001"), default);

        var rep = await new FlujoEfectivoReporteHandler(db, new Reloj())
            .Handle(new FlujoEfectivoReporteQuery(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), default);

        rep.Filas.Where(f => (string?)f["tipo"] == "movimiento").Should().ContainSingle()
            .Which["ingresos"].Should().Be(100_000m);
        rep.Filas.Single(f => (string?)f["tipo"] == "total")["neto"].Should().Be(100_000m);
        rep.Filas.Single(f => (string?)f["tipo"] == "saldoFinal" && (Guid?)f["cuentaId"] == a.Id)["neto"].Should().Be(50_000m);
        rep.Filas.Single(f => (string?)f["tipo"] == "saldoFinal" && (Guid?)f["cuentaId"] == b.Id)["neto"].Should().Be(50_000m);
    }
}
