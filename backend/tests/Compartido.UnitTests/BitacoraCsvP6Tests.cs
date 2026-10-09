using Millet.Administracion.Application.Auditoria;

namespace Millet.Compartido.UnitTests;

public sealed class BitacoraCsvP6Tests
{
    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("  +1", "\"'  +1\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("\t=1", "\"'\t=1\"")]
    [InlineData("a,\"b\"\r\nc", "\"a,\"\"b\"\"\r\nc\"")]
    [InlineData(null, "\"\"")]
    public void Protege_formulas_y_conserva_comillas_comas_y_saltos(string? valor, string esperado)
        => BitacoraCsv.Campo(valor).Should().Be(esperado);
}
