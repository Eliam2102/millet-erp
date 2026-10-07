using Millet.Contabilidad.Application.Periodos;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// F1-CON-03: ejercicio y periodos contables (D2–D4, D7): generación de los 13 periodos, transiciones válidas e inválidas,
/// periodo 13, resolución de fecha, motivo obligatorio, cierre secuencial y reapertura con el siguiente cerrado.
/// Calendario de PRUEBA, no el oficial de Contabilidad.
/// </summary>
public class PeriodosTests
{
    [Theory]
    [InlineData(true, null, null, true)]
    [InlineData(false, 2026, 13, true)]
    [InlineData(true, 2026, null, false)]
    [InlineData(true, null, 1, false)]
    [InlineData(true, 2026, 1, false)]
    [InlineData(false, null, null, false)]
    [InlineData(false, 2026, null, false)]
    [InlineData(false, null, 1, false)]
    [InlineData(false, 1999, 1, false)]
    [InlineData(false, 3000, 1, false)]
    [InlineData(false, 2026, 14, false)]
    public void La_consulta_exige_fecha_exclusiva_o_anio_y_numero_validos(bool conFecha, int? anio, int? numero, bool valido)
    {
        var consulta = new ConsultarEstadoPeriodoQuery(conFecha ? new DateOnly(2026, 1, 15) : null, anio, numero);
        new ConsultarEstadoPeriodoValidator().Validate(consulta).IsValid.Should().Be(valido);
    }

    private static readonly DateTimeOffset Ahora = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private const string Motivo = "FIX cierre mensual de prueba";

    private static List<PeriodoContable> Ejercicio(int anio = 2026) => [.. new EjercicioContable(Guid.NewGuid(), anio).GenerarPeriodos()];

    private static PeriodoContable P(List<PeriodoContable> ps, int n) => ps.Single(p => p.Numero == n);

    private static void Abrir(List<PeriodoContable> ps, params int[] numeros)
    {
        foreach (var n in numeros) P(ps, n).Abrir(P(ps, 12), null, null, "fix", Ahora);
    }

    private static void Cerrar(List<PeriodoContable> ps, params int[] numeros)
    {
        foreach (var n in numeros) P(ps, n).Cerrar(ps, Motivo, null, "fix", Ahora);
    }

    [Fact]
    public void Crear_ejercicio_genera_doce_meses_y_el_periodo_13_sin_abrir()
    {
        var ps = Ejercicio(2028);
        ps.Should().HaveCount(13).And.OnlyContain(p => p.Estado == EstadoPeriodo.NoAbierto && p.Anio == 2028);
        P(ps, 2).FechaInicio.Should().Be(new DateOnly(2028, 2, 1));
        P(ps, 2).FechaFin.Should().Be(new DateOnly(2028, 2, 29));
        P(ps, 12).FechaFin.Should().Be(new DateOnly(2028, 12, 31));
        P(ps, 13).FechaInicio.Should().Be(new DateOnly(2028, 12, 31));
        P(ps, 13).FechaFin.Should().Be(new DateOnly(2028, 12, 31));
        P(ps, 13).EsAjuste.Should().BeTrue();
        PeriodoContable.Nombre(9).Should().Be("Septiembre");
        P(ps, 9).Clave.Should().Be("2028-09");
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(3000)]
    public void Anio_fuera_de_rango_se_rechaza(int anio)
    {
        var act = () => new EjercicioContable(Guid.NewGuid(), anio);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_EJERCICIO_ANIO_INVALIDO");
    }

    [Fact]
    public void Una_fecha_de_diciembre_cae_en_el_12_nunca_en_el_13()
    {
        PeriodoContable.PorFecha(new DateOnly(2026, 12, 31)).Should().Be((2026, 12));
        PeriodoContable.PorFecha(new DateOnly(2026, 9, 15)).Should().Be((2026, 9));
    }

    [Fact]
    public void Abrir_cerrar_y_reabrir_devuelven_datos_para_auditoria_con_la_version_resultante()
    {
        var ps = Ejercicio();
        var enero = P(ps, 1);

        var abrir = enero.Abrir(null, null, null, "fix", Ahora);
        abrir.Accion.Should().Be(AccionPeriodo.Abrir);
        abrir.EstadoAnterior.Should().Be(EstadoPeriodo.NoAbierto);
        abrir.EstadoNuevo.Should().Be(EstadoPeriodo.Abierto);
        abrir.VersionResultante.Should().Be(enero.Version + 1);
        abrir.Motivo.Should().BeNull();

        var usuario = Guid.NewGuid();
        var cerrar = enero.Cerrar(ps, "  " + Motivo + "  ", usuario, "Contadora FIX", Ahora);
        enero.Estado.Should().Be(EstadoPeriodo.Cerrado);
        enero.CerradoPor.Should().Be("Contadora FIX");
        cerrar.Motivo.Should().Be(Motivo);
        cerrar.UsuarioId.Should().Be(usuario);

        var reabrir = enero.Reabrir(P(ps, 2), "FIX ajuste de provisión omitida", null, "fix", Ahora);
        enero.Estado.Should().Be(EstadoPeriodo.Abierto);
        reabrir.EstadoAnterior.Should().Be(EstadoPeriodo.Cerrado);
        enero.ReabiertoEn.Should().Be(Ahora);
    }

    [Fact]
    public void Abrir_un_periodo_ya_abierto_o_cerrado_se_rechaza()
    {
        var ps = Ejercicio();
        Abrir(ps, 1);
        var otraVez = () => P(ps, 1).Abrir(null, null, null, "fix", Ahora);
        otraVez.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_PERIODO_NO_ABRIBLE");
        Cerrar(ps, 1);
        otraVez.Should().Throw<BusinessRuleException>().Which.Message.Should().Contain("reabrirlo");
    }

    [Fact]
    public void Cerrar_ya_cerrado_es_conflicto_explicito_y_sin_abrir_no_se_cierra()
    {
        var ps = Ejercicio();
        var sinAbrir = () => P(ps, 1).Cerrar(ps, Motivo, null, "fix", Ahora);
        sinAbrir.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_PERIODO_NO_ABIERTO");

        Abrir(ps, 1);
        Cerrar(ps, 1);
        var repetido = () => P(ps, 1).Cerrar(ps, Motivo, null, "fix", Ahora);
        repetido.Should().Throw<ConflictException>().Which.Code.Should().Be("CONTAB_PERIODO_YA_CERRADO");
    }

    [Fact]
    public void El_cierre_es_secuencial_y_la_apertura_no_exige_orden()
    {
        var ps = Ejercicio();
        Abrir(ps, 3, 1, 2);
        var marzo = () => P(ps, 3).Cerrar(ps, Motivo, null, "fix", Ahora);
        var ex = marzo.Should().Throw<BusinessRuleException>().Which;
        ex.Code.Should().Be("CONTAB_PERIODO_ANTERIOR_ABIERTO");
        ex.Message.Should().Contain("2026-01");

        Cerrar(ps, 1, 2);
        marzo.Should().NotThrow();
    }

    [Fact]
    public void Reabrir_exige_que_este_cerrado_y_que_el_siguiente_no_lo_este()
    {
        var ps = Ejercicio();
        Abrir(ps, 1, 2);
        var abierto = () => P(ps, 1).Reabrir(P(ps, 2), Motivo, null, "fix", Ahora);
        abierto.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_PERIODO_NO_CERRADO");

        Cerrar(ps, 1, 2);
        var enero = () => P(ps, 1).Reabrir(P(ps, 2), Motivo, null, "fix", Ahora);
        var ex = enero.Should().Throw<BusinessRuleException>().Which;
        ex.Code.Should().Be("CONTAB_PERIODO_SIGUIENTE_CERRADO");
        ex.Message.Should().Contain("Febrero");

        P(ps, 2).Reabrir(P(ps, 3), Motivo, null, "fix", Ahora);
        enero.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("corto")]
    [InlineData("         x         ")]
    public void Cerrar_y_reabrir_exigen_motivo_de_10_a_500_caracteres(string? motivo)
    {
        var ps = Ejercicio();
        Abrir(ps, 1);
        var cerrar = () => P(ps, 1).Cerrar(ps, motivo!, null, "fix", Ahora);
        cerrar.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_PERIODO_MOTIVO_INVALIDO");
        P(ps, 1).Estado.Should().Be(EstadoPeriodo.Abierto);

        var largo = () => P(ps, 1).Cerrar(ps, new string('x', 501), null, "fix", Ahora);
        largo.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_PERIODO_MOTIVO_INVALIDO");

        Cerrar(ps, 1);
        var reabrir = () => P(ps, 1).Reabrir(P(ps, 2), motivo!, null, "fix", Ahora);
        reabrir.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_PERIODO_MOTIVO_INVALIDO");
    }

    [Fact]
    public void Periodo_13_solo_se_abre_con_diciembre_cerrado_y_solo_admite_movimientos_manuales()
    {
        var ps = Ejercicio();
        var trece = () => P(ps, 13).Abrir(P(ps, 12), null, null, "fix", Ahora);
        trece.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_PERIODO_13_REQUIERE_12_CERRADO");
        Abrir(ps, [.. Enumerable.Range(1, 12)]);
        trece.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_PERIODO_13_REQUIERE_12_CERRADO");

        Cerrar(ps, [.. Enumerable.Range(1, 12)]);
        trece.Should().NotThrow();
        P(ps, 13).AdmiteOrigen(OrigenMovimiento.Manual).Should().BeTrue();
        P(ps, 13).AdmiteOrigen(OrigenMovimiento.AuxiliarCxC).Should().BeFalse();
        P(ps, 12).AdmiteOrigen(OrigenMovimiento.AuxiliarCxP).Should().BeTrue();

        // Diciembre no se reabre mientras el 13 esté cerrado.
        Cerrar(ps, 13);
        var diciembre = () => P(ps, 12).Reabrir(P(ps, 13), Motivo, null, "fix", Ahora);
        diciembre.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_PERIODO_SIGUIENTE_CERRADO");
    }

    [Theory]
    [InlineData(false, EstadoPeriodo.NoAbierto, 9, OrigenMovimiento.Manual, "CONTAB_PERIODO_INEXISTENTE")]
    [InlineData(true, EstadoPeriodo.NoAbierto, 9, OrigenMovimiento.Manual, "CONTAB_PERIODO_NO_ABIERTO")]
    [InlineData(true, EstadoPeriodo.Cerrado, 9, OrigenMovimiento.Manual, "CONTAB_PERIODO_CERRADO")]
    [InlineData(true, EstadoPeriodo.Abierto, 13, OrigenMovimiento.AuxiliarCxP, "CONTAB_PERIODO_13_SOLO_MANUAL")]
    [InlineData(true, EstadoPeriodo.Abierto, 13, OrigenMovimiento.Manual, null)]
    [InlineData(true, EstadoPeriodo.Abierto, 9, OrigenMovimiento.AuxiliarCxC, null)]
    public void El_verificador_falla_cerrado(bool existe, EstadoPeriodo estado, int numero, OrigenMovimiento origen, string? codigo)
    {
        var p = new EstadoPeriodoContable(2026, numero, estado, existe);
        p.AdmiteMovimientos.Should().Be(existe && estado == EstadoPeriodo.Abierto);
        var rechazo = VerificadorPeriodoContable.Rechazo(p, origen);
        rechazo?.Code.Should().Be(codigo);
        if (codigo is null) rechazo.Should().BeNull();
        if (codigo == "CONTAB_PERIODO_CERRADO") rechazo!.Message.Should().Be("El periodo 2026-09 está cerrado; no se pueden registrar movimientos con esa fecha.");
    }
}
