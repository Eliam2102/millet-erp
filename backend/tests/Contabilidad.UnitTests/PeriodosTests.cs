using Millet.Contabilidad.Application.Periodos;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.UnitTests;

/// <summary>F1-CON-03 (C1.1): periodos contables, Módulo 10 R18/R19.</summary>
public class PeriodosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Los_periodos_1_a_12_son_los_meses_y_el_13_no_tiene_fechas()
    {
        var feb = new PeriodoContable(Guid.CreateVersion7(), 2028, 2);
        feb.FechaInicio.Should().Be(new DateOnly(2028, 2, 1));
        feb.FechaFin.Should().Be(new DateOnly(2028, 2, 29), "2028 es bisiesto");
        feb.Abierto.Should().BeTrue();

        var trece = new PeriodoContable(Guid.CreateVersion7(), 2026, 13);
        trece.EsPeriodoAjustes.Should().BeTrue();
        trece.FechaInicio.Should().BeNull();
        trece.FechaFin.Should().BeNull();
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 14)]
    [InlineData(1999, 1)]
    [InlineData(2101, 1)]
    public void Ejercicio_o_numero_fuera_de_rango_se_rechaza(int ejercicio, int numero) =>
        ((Func<PeriodoContable>)(() => new PeriodoContable(Guid.CreateVersion7(), ejercicio, numero))).Should().Throw<BusinessRuleException>();

    [Fact]
    public void Cerrar_y_reabrir_dejan_quien_y_cuando()
    {
        var p = new PeriodoContable(Guid.CreateVersion7(), 2026, 1);
        p.Cerrar("contador", Ahora);
        p.Estado.Should().Be(EstadoPeriodo.Cerrado);
        p.CerradoPor.Should().Be("contador");
        p.CerradoEn.Should().Be(Ahora);

        p.Reabrir("contador", "Ajuste de provisión", Ahora.AddHours(1));
        p.Estado.Should().Be(EstadoPeriodo.Abierto);
        p.ReabiertoPor.Should().Be("contador");
        p.ReabiertoEn.Should().Be(Ahora.AddHours(1));
        p.CerradoPor.Should().Be("contador", "se conserva quién lo cerró la última vez");
    }

    [Fact]
    public void No_se_cierra_dos_veces_ni_se_reabre_un_periodo_abierto()
    {
        var p = new PeriodoContable(Guid.CreateVersion7(), 2026, 1);
        ((Action)(() => p.Reabrir("x", "motivo", Ahora))).Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONTAB_PERIODO_YA_ABIERTO");
        p.Cerrar("x", Ahora);
        ((Action)(() => p.Cerrar("x", Ahora))).Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONTAB_PERIODO_YA_CERRADO");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reabrir_exige_motivo(string? motivo)
    {
        var p = new PeriodoContable(Guid.CreateVersion7(), 2026, 1);
        p.Cerrar("x", Ahora);
        ((Action)(() => p.Reabrir("x", motivo, Ahora))).Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONTAB_PERIODO_MOTIVO_REQUERIDO");
    }

    [Fact]
    public void Validadores_de_comandos()
    {
        new CrearEjercicioValidator().Validate(new CrearEjercicioCommand(2026)).IsValid.Should().BeTrue();
        new CrearEjercicioValidator().Validate(new CrearEjercicioCommand(1990)).IsValid.Should().BeFalse();
        new ReabrirPeriodoValidator().Validate(new ReabrirPeriodoCommand(Guid.NewGuid(), 1, "")).IsValid.Should().BeFalse();
        new ReabrirPeriodoValidator().Validate(new ReabrirPeriodoCommand(Guid.NewGuid(), 1, new string('x', 501))).IsValid.Should().BeFalse();
        new CerrarPeriodoValidator().Validate(new CerrarPeriodoCommand(Guid.NewGuid(), 1, null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void El_historial_no_admite_motivos_demasiado_largos() =>
        ((Func<PeriodoContableEvento>)(() => new PeriodoContableEvento(Guid.CreateVersion7(), Guid.NewGuid(), AccionPeriodo.Reabierto, "x", Ahora, new string('x', 501))))
            .Should().Throw<BusinessRuleException>();
}
