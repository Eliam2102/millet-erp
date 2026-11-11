using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.SharedKernel.Application.Exceptions;
using Millet.CuentasPorPagar.Application.TarjetaCredito.EstadosCuenta;
using Millet.CuentasPorPagar.Infrastructure.TarjetaCredito;

namespace Millet.CuentasPorPagar.UnitTests.TarjetaCredito;

/// <summary>
/// F7-PR5: tests del parser de Excel — solo el helper de resolución de
/// columna; el parser completo se valida con archivos sample en
/// IntegrationTests (fuera del scope MVP de unit tests).
/// </summary>
public sealed class EstadoCuentaTcExcelParserTests
{
    [Fact]
    public async Task Parser_reporta_excel_ilegible_como_error_de_negocio()
    {
        var parser = new EstadoCuentaTcExcelParser(NullLogger<EstadoCuentaTcExcelParser>.Instance);
        var perfil = PerfilParserBanco.Crear(Guid.NewGuid(), "QA", "Prueba", "XLSX", "UTF-8", 1,
            "A", "yyyy-MM-dd", "B", null, "C", null, null, "NegativoEsRefund", "es-MX");
        using var contenido = new MemoryStream("texto sin estructura Excel"u8.ToArray());

        var intento = () => parser.ParseAsync(contenido, perfil, default);

        (await intento.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("EC_ARCHIVO_ILEGIBLE");
    }

    [Theory]
    [InlineData("estado.xlsx", true)]
    [InlineData("estado.XLSX", true)]
    [InlineData("estado.xls", false)]
    public void Carga_solo_admite_la_extension_del_lector(string archivo, bool valido)
    {
        var comando = new SubirArchivoEstadoCuentaTcCommand(Guid.NewGuid(), 1, archivo, "application/octet-stream", [1]);
        new SubirArchivoEstadoCuentaTcValidator().Validate(comando).IsValid.Should().Be(valido);
    }

    [Theory]
    [InlineData("A", 1)]
    [InlineData("B", 2)]
    [InlineData("Z", 26)]
    [InlineData("AA", 27)]
    [InlineData("AZ", 52)]
    [InlineData("BA", 53)]
    [InlineData("1", 1)]
    [InlineData("3", 3)]
    public void ResolverColumna_acepta_letra_y_indice(string raw, int expected)
    {
        EstadoCuentaTcExcelParser.ResolverColumna(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("@")]
    [InlineData("0")]
    public void ResolverColumna_rechaza_invalido(string raw)
    {
        var act = () => EstadoCuentaTcExcelParser.ResolverColumna(raw);
        act.Should().Throw<InvalidOperationException>();
    }
}
