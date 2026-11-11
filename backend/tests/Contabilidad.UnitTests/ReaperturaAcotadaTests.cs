using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// M-213: reapertura acotada del periodo. Usuarios, tipos y cuentas genéricos (el escenario de la ficha: un periodo reabierto solo
/// para un ajuste de inventario de una persona; una póliza de ventas o de otro usuario se rechaza; al vencer vuelve a cerrarse).
/// Calendario de PRUEBA, no el oficial de Contabilidad.
/// </summary>
public class ReaperturaAcotadaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Capturista = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid OtroUsuario = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private static PeriodoContable PeriodoCerradoYReabierto()
    {
        var ps = new EjercicioContable(Guid.NewGuid(), 2026).GenerarPeriodos().ToList();
        var p12 = ps.Single(p => p.Numero == 12);
        var octubre = ps.Single(p => p.Numero == 10);
        octubre.Abrir(p12, null, null, "fix", Ahora);
        // Cierre secuencial: se abren y cierran 1–9 antes de cerrar octubre.
        foreach (var n in Enumerable.Range(1, 9)) { var x = ps.Single(p => p.Numero == n); x.Abrir(p12, null, null, "fix", Ahora); x.Cerrar(ps, "FIX cierre de prueba", null, "fix", Ahora); }
        octubre.Cerrar(ps, "FIX cierre de prueba", null, "fix", Ahora);
        octubre.Reabrir(ps.Single(p => p.Numero == 11), "FIX reapertura de prueba", null, "fix", Ahora);
        return octubre;
    }

    private static ReaperturaPeriodo Acotada(PeriodoContable p, int maximoDias = 30, DateTimeOffset? vence = null) =>
        new(Guid.NewGuid(), p, ["ai"], ["1150"], ["almacen"], [Capturista], "Oficio de autorización 001/2026", vence ?? Ahora.AddDays(5), maximoDias, "Contador General", Ahora);

    private static ContextoRegistroPoliza Registro(string tipo = "AI", Guid? usuario = null, string? modulo = "Almacen", params string[] cuentas) =>
        new(tipo, usuario ?? Capturista, modulo, cuentas.Length == 0 ? ["11500001"] : cuentas);

    [Fact]
    public void El_ajuste_dentro_del_alcance_entra()
    {
        var r = Acotada(PeriodoCerradoYReabierto());
        r.Rechazo(Registro()).Should().BeNull();
    }

    [Fact]
    public void Una_poliza_de_ventas_se_rechaza_con_mensaje_claro()
    {
        var r = Acotada(PeriodoCerradoYReabierto());
        var rechazo = r.Rechazo(Registro(tipo: "VT"));
        rechazo.Should().NotBeNull();
        rechazo!.Code.Should().Be("CONTAB_PERIODO_REAPERTURA_FUERA_DE_ALCANCE");
        rechazo.Message.Should().Contain("2026-10").And.Contain("AI");
    }

    [Fact]
    public void Una_poliza_de_otro_usuario_se_rechaza()
    {
        var r = Acotada(PeriodoCerradoYReabierto());
        r.Rechazo(Registro(usuario: OtroUsuario))!.Message.Should().Contain("usuarios autorizados");
    }

    [Fact]
    public void Sin_usuario_identificado_se_rechaza_si_la_reapertura_restringe_usuarios()
    {
        var r = Acotada(PeriodoCerradoYReabierto());
        r.Rechazo(new ContextoRegistroPoliza("AI", null, "Almacen", ["11500001"])).Should().NotBeNull();
    }

    [Fact]
    public void Una_cuenta_fuera_de_los_prefijos_o_un_modulo_distinto_se_rechazan()
    {
        var r = Acotada(PeriodoCerradoYReabierto());
        r.Rechazo(Registro(cuentas: ["11500001", "40100001"]))!.Message.Should().Contain("1150");
        r.Rechazo(Registro(modulo: "Ventas"))!.Message.Should().Contain("ALMACEN");
        r.Rechazo(Registro(modulo: null)).Should().NotBeNull();
    }

    [Fact]
    public void Una_operacion_que_no_informa_que_registra_falla_cerrada()
    {
        Acotada(PeriodoCerradoYReabierto()).Rechazo(null).Should().NotBeNull();
    }

    [Fact]
    public void Una_dimension_vacia_no_restringe_y_sin_ninguna_la_reapertura_solo_tiene_vencimiento()
    {
        var p = PeriodoCerradoYReabierto();
        var soloTipo = new ReaperturaPeriodo(Guid.NewGuid(), p, ["AI"], null, null, null, "Oficio de autorización 002/2026", Ahora.AddDays(1), 30, "Contador General", Ahora);
        soloTipo.Rechazo(Registro(usuario: OtroUsuario, modulo: null)).Should().BeNull();
        soloTipo.Rechazo(Registro(tipo: "VT")).Should().NotBeNull();
        var libre = new ReaperturaPeriodo(Guid.NewGuid(), p, null, null, null, null, "Oficio de autorización 003/2026", Ahora.AddDays(1), 30, "Contador General", Ahora);
        libre.TieneAlcance.Should().BeFalse();
        libre.Rechazo(null).Should().BeNull();
    }

    [Fact]
    public void Al_vencer_la_reapertura_con_la_hora_del_servidor_deja_de_estar_vigente_el_periodo()
    {
        var r = Acotada(PeriodoCerradoYReabierto(), vence: Ahora.AddHours(2));
        r.Vencida(Ahora.AddHours(1)).Should().BeFalse();
        r.Vencida(Ahora.AddHours(2)).Should().BeTrue();
        r.Concluir(Ahora.AddHours(3));
        r.Vigente.Should().BeFalse();
        r.Vencida(Ahora.AddDays(9)).Should().BeFalse("una reapertura concluida ya no aplica");
    }

    [Fact]
    public void El_cierre_por_vencimiento_deja_el_periodo_cerrado_por_el_sistema_y_la_bitacora()
    {
        var p = PeriodoCerradoYReabierto();
        var t = p.CerrarPorVencimiento(Ahora.AddDays(5), "sistema");
        p.Estado.Should().Be(EstadoPeriodo.Cerrado);
        p.CerradoPor.Should().Be("sistema");
        p.ReabiertoPor.Should().BeNull();
        t.Accion.Should().Be(AccionPeriodo.Cerrar);
        t.Motivo.Should().Contain("venció la reapertura");
        FluentActions.Invoking(() => p.CerrarPorVencimiento(Ahora.AddDays(6), "sistema")).Should().Throw<BusinessRuleException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("abc")]
    public void La_evidencia_es_obligatoria(string? evidencia)
    {
        var p = PeriodoCerradoYReabierto();
        FluentActions.Invoking(() => new ReaperturaPeriodo(Guid.NewGuid(), p, null, null, null, null, evidencia, Ahora.AddDays(1), 30, "x", Ahora))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_REAPERTURA_EVIDENCIA_INVALIDA");
    }

    [Fact]
    public void El_vencimiento_debe_ser_futuro_y_respeta_el_maximo_configurable()
    {
        var p = PeriodoCerradoYReabierto();
        FluentActions.Invoking(() => Acotada(p, vence: Ahora)).Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONTAB_REAPERTURA_VENCIMIENTO_INVALIDO");
        FluentActions.Invoking(() => Acotada(p, maximoDias: 3, vence: Ahora.AddDays(4))).Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONTAB_REAPERTURA_VENCIMIENTO_EXCEDE");
        FluentActions.Invoking(() => Acotada(p, maximoDias: 0, vence: Ahora.AddDays(400))).Should().NotThrow("0 = sin tope");
    }
}
