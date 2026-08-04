using Millet.CuentasPorCobrar.Application.Cobranza;
using Millet.CuentasPorCobrar.Domain.Cobranza;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.Cobranza;

public class SeguimientoCobranzaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);

    private static SeguimientoCobranza Registrar(
        ResultadoCobranza resultado = ResultadoCobranza.SinRespuesta,
        decimal? monto = null,
        DateOnly? fechaComprometida = null,
        string nota = "Se llamó al cliente, no contestó.") =>
        SeguimientoCobranza.Registrar(
            empresaId: Guid.NewGuid(),
            clienteId: Guid.NewGuid(),
            usuarioId: Guid.NewGuid(),
            canal: CanalCobranza.Llamada,
            resultado: resultado,
            montoComprometido: monto,
            fechaComprometida: fechaComprometida,
            nota: nota,
            fecha: Ahora);

    [Fact]
    public void Registrar_gestion_simple_OK()
    {
        var s = Registrar();

        s.Resultado.Should().Be(ResultadoCobranza.SinRespuesta);
        s.MontoComprometido.Should().BeNull();
        s.FechaComprometida.Should().BeNull();
    }

    [Fact]
    public void PromesaPago_con_monto_y_fecha_OK()
    {
        var s = Registrar(
            resultado: ResultadoCobranza.PromesaPago,
            monto: 50_000m,
            fechaComprometida: new DateOnly(2026, 7, 20),
            nota: "Cliente promete pagar el 20.");

        s.MontoComprometido.Should().Be(50_000m);
        s.FechaComprometida.Should().Be(new DateOnly(2026, 7, 20));
    }

    [Fact]
    public void PromesaPago_exige_monto()
    {
        var act = () => Registrar(
            resultado: ResultadoCobranza.PromesaPago,
            fechaComprometida: new DateOnly(2026, 7, 20));
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "SC_PROMESA_SIN_MONTO");
    }

    [Fact]
    public void PromesaPago_exige_fecha()
    {
        var act = () => Registrar(resultado: ResultadoCobranza.PromesaPago, monto: 100m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "SC_PROMESA_SIN_FECHA");
    }

    [Fact]
    public void PromesaPago_rechaza_fecha_pasada()
    {
        var act = () => Registrar(
            resultado: ResultadoCobranza.PromesaPago,
            monto: 100m,
            fechaComprometida: new DateOnly(2026, 7, 1));
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "SC_PROMESA_FECHA_PASADA");
    }

    [Fact]
    public void Gestion_sin_promesa_rechaza_monto_o_fecha_sobrantes()
    {
        var act = () => Registrar(resultado: ResultadoCobranza.Excusa, monto: 100m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "SC_COMPROMISO_SOBRANTE");
    }

    [Fact]
    public void Nota_es_obligatoria()
    {
        var act = () => Registrar(nota: "  ");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "SC_NOTA_VACIA");
    }

    [Fact]
    public void Validator_exige_monto_y_fecha_en_promesa()
    {
        var validator = new RegistrarSeguimientoCobranzaValidator();
        var result = validator.Validate(new RegistrarSeguimientoCobranzaCommand(
            Guid.NewGuid(), CanalCobranza.Correo, ResultadoCobranza.PromesaPago, null, null, "nota"));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.PropertyName).Should().Contain(
            ["MontoComprometido", "FechaComprometida"]);
    }
}
