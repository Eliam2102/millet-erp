using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Infrastructure.Adapters;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.AplicacionPagos;

/// <summary>
/// M-326: un cobro corto de hasta $0.99 (en la moneda del cobro) es redondeo: va a la cuenta REDONDEO sin motivo. Parte 2 (c): su
/// autorización la da el Gerente de Área (otra persona, no quien propone). Más de $0.99 sigue como hoy (tipo, motivo y autorización).
/// El redondeo lo asigna el sistema; no se elige a mano.
/// </summary>
public sealed class RedondeoCobroTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid Proponente = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Autorizador = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Tesoreria = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private static PropuestaAplicacionPago Crear(decimal faltante, string moneda = "MXN", TipoAjusteCobro? tipo = null, string? motivo = null) =>
        PropuestaAplicacionPago.Crear(EmpresaId, ClienteId, "DEP-1", 10_000m - faltante, moneda, "REM-1",
            [("uuid-1", Guid.NewGuid(), 10_000m, 1)], 50m, Proponente, tipoAjuste: tipo, motivoAjuste: motivo, ahora: Ahora);

    [Fact]
    public void Un_faltante_de_040_es_redondeo_sin_motivo_y_espera_la_autorizacion()
    {
        var p = Crear(0.40m);
        p.AjusteNoFiscal.Should().Be(-0.40m);
        p.TipoAjuste.Should().Be(TipoAjusteCobro.Redondeo);
        p.AjustePorAutorizar.Should().BeTrue("M-326 parte 2: el redondeo espera al Gerente de Área; Tesorería no la ve antes");
        p.RequiereGerenteDeArea.Should().BeTrue();
        p.AjusteAutorizadoEn.Should().BeNull();
        p.AjusteAutorizadoPor.Should().BeNull();
    }

    [Fact]
    public void M326b_Un_excedente_de_040_es_redondeo_y_no_saldo_a_favor_y_espera_al_gerente()
    {
        var p = Crear(-0.40m); // depósito 10,000.40 contra 10,000 aplicados
        p.AjusteNoFiscal.Should().Be(0.40m);
        p.SaldoAFavorPorIdentificar.Should().Be(0m);
        p.TipoAjuste.Should().Be(TipoAjusteCobro.Redondeo);
        p.AjustePorAutorizar.Should().BeTrue();
        p.RequiereGerenteDeArea.Should().BeTrue();

        p.AutorizarAjuste(Autorizador, Ahora);
        p.AjusteAutorizadoPor.Should().Be(Autorizador);
    }

    [Fact]
    public void M326b_Un_excedente_mayor_a_099_sigue_como_saldo_a_favor()
    {
        var p = Crear(-1.20m);
        p.SaldoAFavorPorIdentificar.Should().Be(1.20m);
        p.AjusteNoFiscal.Should().Be(0m);
        p.TipoAjuste.Should().BeNull();
        p.AjustePorAutorizar.Should().BeFalse();
        Crear(-0.99m).TipoAjuste.Should().Be(TipoAjusteCobro.Redondeo);
    }

    [Fact]
    public void M326b_El_excedente_de_redondeo_no_se_revierte_como_faltante()
    {
        var p = Crear(-0.40m);
        p.AutorizarAjuste(Autorizador, Ahora);
        p.Confirmar(Tesoreria, Ahora, Guid.NewGuid());
        p.MarcarReppTimbrado(Guid.NewGuid());
        var revertir = () => p.RevertirAjuste(Autorizador, "No procede", Ahora);
        revertir.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_SIN_AJUSTE_AUTORIZADO");
    }

    [Theory]
    [InlineData("MXN")]
    [InlineData("USD")]
    public void El_limite_es_099_en_cualquier_moneda_y_100_ya_no_es_redondeo(string moneda)
    {
        Crear(0.99m, moneda).TipoAjuste.Should().Be(TipoAjusteCobro.Redondeo);
        Crear(0.01m, moneda).TipoAjuste.Should().Be(TipoAjusteCobro.Redondeo);

        var uno = () => Crear(1.00m, moneda);
        uno.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_AJUSTE_SIN_TIPO");
        var unoVeinte = () => Crear(1.20m, moneda);
        unoVeinte.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_AJUSTE_SIN_TIPO");
    }

    [Fact]
    public void Mas_de_099_documentado_sigue_por_autorizar_y_no_es_redondeo()
    {
        var p = Crear(1.20m, tipo: TipoAjusteCobro.DiferenciaMenor, motivo: "Diferencia menor documentada");
        p.TipoAjuste.Should().Be(TipoAjusteCobro.DiferenciaMenor);
        p.AjustePorAutorizar.Should().BeTrue();
    }

    [Theory]
    [InlineData(0.40)]
    [InlineData(1.20)]
    public void El_redondeo_no_se_elige_a_mano(double faltante)
    {
        var act = () => Crear((decimal)faltante, tipo: TipoAjusteCobro.Redondeo, motivo: "a mano");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_AJUSTE_TIPO_INVALIDO");
    }

    [Fact]
    public void Con_tipo_documentado_explicito_se_respeta_aunque_sea_menor_a_099()
    {
        var comision = Crear(0.40m, tipo: TipoAjusteCobro.ComisionBancaria, motivo: "Comisión del banco");
        comision.TipoAjuste.Should().Be(TipoAjusteCobro.ComisionBancaria);
        comision.AjustePorAutorizar.Should().BeTrue();

        var sinMotivo = () => Crear(0.40m, tipo: TipoAjusteCobro.DiferenciaMenor, motivo: " ");
        sinMotivo.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_AJUSTE_SIN_MOTIVO");
    }

    [Fact]
    public void El_redondeo_no_depende_de_la_tolerancia_no_fiscal_de_la_moneda()
    {
        // Moneda sin tolerancia configurada (0): el faltante mayor se rechaza, el redondeo de centavos no.
        var p = PropuestaAplicacionPago.Crear(EmpresaId, ClienteId, "DEP-2", 9_999.60m, "EUR", "REM-2",
            [("uuid-2", Guid.NewGuid(), 10_000m, 1)], 0m, Proponente, ahora: Ahora);
        p.TipoAjuste.Should().Be(TipoAjusteCobro.Redondeo);
        var mayor = () => PropuestaAplicacionPago.Crear(EmpresaId, ClienteId, "DEP-3", 9_990m, "EUR", "REM-3",
            [("uuid-3", Guid.NewGuid(), 10_000m, 1)], 0m, Proponente, ahora: Ahora);
        mayor.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_TOLERANCIA_EXCEDIDA");
    }

    [Fact]
    public void El_soporte_adjunto_sigue_exigido_solo_para_la_comision_bancaria()
    {
        var diferencia = Crear(5m, tipo: TipoAjusteCobro.DiferenciaMenor, motivo: "Diferencia menor documentada");
        diferencia.AutorizarAjuste(Autorizador, Ahora, tieneSoporte: false);
        diferencia.AjusteAutorizadoPor.Should().Be(Autorizador);

        var comision = Crear(5m, tipo: TipoAjusteCobro.ComisionBancaria, motivo: "Comisión del banco");
        var sinSoporte = () => comision.AutorizarAjuste(Autorizador, Ahora, tieneSoporte: false);
        sinSoporte.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_AJUSTE_SIN_SOPORTE");
    }

    [Fact]
    public async Task Contabilidad_recibe_el_redondeo_como_ajuste_contable_de_tipo_redondeo()
    {
        using var db = CrearDbContext();
        var p = Crear(0.40m);
        db.PropuestasAplicacionPago.Add(p);
        await db.SaveChangesAsync();

        (await new AjusteCobroContableAdapter(db).ObtenerAsync(EmpresaId, p.Id, default)).Should().BeNull("sin autorización no hay ajuste contable");
        p.AutorizarAjuste(Autorizador, Ahora);
        await db.SaveChangesAsync();
        var ajuste = await new AjusteCobroContableAdapter(db).ObtenerAsync(EmpresaId, p.Id, default);

        ajuste.Should().NotBeNull();
        ajuste!.Tipo.Should().Be(TipoAjusteCobroContable.Redondeo);
        ajuste.Importe.Should().Be(0.40m);
    }

    [Fact]
    public async Task Revertir_un_redondeo_sin_soporte_genera_la_reversa_de_tipo_redondeo_con_otra_persona()
    {
        using var db = CrearDbContext();
        var p = Crear(0.40m);
        p.AutorizarAjuste(Autorizador, Ahora);
        p.Confirmar(Tesoreria, Ahora, Guid.NewGuid());
        p.MarcarReppTimbrado(Guid.NewGuid());
        db.PropuestasAplicacionPago.Add(p);
        await db.SaveChangesAsync();

        var mismo = () => p.RevertirAjuste(Proponente, "Se cobró el resto", Ahora);
        mismo.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_REVERSA_MISMO_USUARIO");
        p.RevertirAjuste(Autorizador, "Se cobró el resto", Ahora);
        await db.SaveChangesAsync();

        var reversa = await new AjusteCobroContableAdapter(db).ObtenerReversaAsync(EmpresaId, p.Id, default);
        reversa.Should().NotBeNull();
        reversa!.Tipo.Should().Be(TipoAjusteCobroContable.Redondeo);
        reversa.Importe.Should().Be(0.40m);
    }

    private static CuentasPorCobrarDbContext CrearDbContext() =>
        new(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase($"cxc_m326_{Guid.NewGuid()}").Options, new FakeEmpresaContext(EmpresaId));

    private sealed class FakeEmpresaContext(Guid empresaId) : Millet.SharedKernel.Application.ICurrentEmpresaContext
    {
        public Guid? Current => empresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
