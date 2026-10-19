using Millet.Almacen.Domain.Revision;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Revision;

public class DictamenRevisionTests
{
    private static DictamenRevision Crear(
        ResultadoDictamen r = ResultadoDictamen.Liberar, decimal cantidad = 1m, string motivo = "Ok",
        Guid? usuario = null, Guid? salida = null, Guid? entrada = null, Guid? dev = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), r, cantidad, motivo,
            usuario ?? Guid.NewGuid(), salida, entrada, dev);

    [Fact]
    public void Liberar_valido_requiere_salida_y_entrada()
    {
        var d = Crear(salida: Guid.NewGuid(), entrada: Guid.NewGuid());
        d.Resultado.Should().Be(ResultadoDictamen.Liberar);
    }

    [Fact]
    public void Baja_y_rechazar_validos()
    {
        Crear(ResultadoDictamen.Baja, salida: Guid.NewGuid());
        Crear(ResultadoDictamen.Rechazar, dev: Guid.NewGuid());
    }

    [Theory]
    [InlineData(0, "Ok", "DICTAMEN_CANTIDAD_INVALIDA")]
    [InlineData(1, " ", "DICTAMEN_MOTIVO_INVALIDO")]
    public void Datos_invalidos_lanzan_codigo(double cantidad, string motivo, string code)
    {
        var act = () => Crear(cantidad: (decimal)cantidad, motivo: motivo, salida: Guid.NewGuid(), entrada: Guid.NewGuid());
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be(code);
    }

    [Fact]
    public void Motivo_largo_y_sin_usuario_y_vinculos_invalidos()
    {
        var s = Guid.NewGuid();
        var e = Guid.NewGuid();
        ((Action)(() => Crear(motivo: new string('x', 501), salida: s, entrada: e)))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("DICTAMEN_MOTIVO_INVALIDO");
        ((Action)(() => Crear(usuario: Guid.Empty, salida: s, entrada: e)))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("DICTAMEN_SIN_USUARIO");
        ((Action)(() => Crear(salida: s)))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("DICTAMEN_VINCULO_INVALIDO");
        ((Action)(() => Crear(ResultadoDictamen.Baja, salida: s, entrada: e)))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("DICTAMEN_VINCULO_INVALIDO");
        ((Action)(() => Crear(ResultadoDictamen.Rechazar)))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("DICTAMEN_VINCULO_INVALIDO");
    }
}
