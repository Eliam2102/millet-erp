using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application;
using Millet.Integraciones.Aw.Application.Commands.RegistrarCotizacionEdi;

namespace Millet.Integraciones.Aw.UnitTests.Application;

/// <summary>
/// Tests del validator del comando. Foco: validación de Sucursal contra
/// catálogo configurado en <see cref="IntegracionesAwOptions.Sucursales"/>.
/// El resto de reglas (QuoteReference, EdiContent, CustomerTaxId, etc.)
/// quedan cubiertas por integration tests del endpoint.
/// </summary>
public sealed class RegistrarCotizacionEdiValidatorTests
{
    [Theory]
    [InlineData("CIR")]
    [InlineData("CHI")]
    [InlineData("CAN")]
    [InlineData("CON")]
    public void Sucursal_CodigoValido_Pasa(string sucursal)
    {
        var validator = BuildValidator();
        var command = BuildCommand(sucursal);

        var result = validator.Validate(command);

        result.Errors.Should().NotContain(e =>
            e.PropertyName == nameof(RegistrarCotizacionEdiCommand.Sucursal));
    }

    [Theory]
    [InlineData("cir")]
    [InlineData("Chi")]
    [InlineData("CaN")]
    public void Sucursal_CaseInsensitive_Pasa(string sucursal)
    {
        // El handler normaliza a uppercase tras la validación; el validator
        // acepta cualquier case (Agent puede mandar 'cir' o 'CIR').
        var validator = BuildValidator();
        var command = BuildCommand(sucursal);

        var result = validator.Validate(command);

        result.Errors.Should().NotContain(e =>
            e.PropertyName == nameof(RegistrarCotizacionEdiCommand.Sucursal));
    }

    [Fact]
    public void Sucursal_Vacia_FallaConCodigoRequerida()
    {
        var validator = BuildValidator();
        var command = BuildCommand(sucursal: "");

        var result = validator.Validate(command);

        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(RegistrarCotizacionEdiCommand.Sucursal)
            && e.ErrorMessage.Contains("AW_SUCURSAL_REQUERIDA"));
    }

    [Fact]
    public void Sucursal_Whitespace_FallaConCodigoRequerida()
    {
        var validator = BuildValidator();
        var command = BuildCommand(sucursal: "   ");

        var result = validator.Validate(command);

        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(RegistrarCotizacionEdiCommand.Sucursal));
    }

    [Theory]
    [InlineData("ZZZ")]
    [InlineData("CDMX")]
    [InlineData("CIRCUITO")] // nombre completo, no código
    public void Sucursal_FueraDeCatalogo_FallaConCodigoInvalida(string sucursal)
    {
        var validator = BuildValidator();
        var command = BuildCommand(sucursal);

        var result = validator.Validate(command);

        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(RegistrarCotizacionEdiCommand.Sucursal)
            && e.ErrorMessage.Contains("AW_SUCURSAL_INVALIDA"));
    }

    [Fact]
    public void Sucursal_CatalogoVacio_RechazaCualquierValor()
    {
        // Defensive: si el config se carga vacío (settings rotos), ningún
        // valor pasa. Preferimos fail loud a aceptar valores sin sentido.
        var validator = BuildValidatorWithCatalog(new());
        var command = BuildCommand(sucursal: "CIR");

        var result = validator.Validate(command);

        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(RegistrarCotizacionEdiCommand.Sucursal)
            && e.ErrorMessage.Contains("AW_SUCURSAL_INVALIDA"));
    }

    // ─── Helpers ───

    private static RegistrarCotizacionEdiValidator BuildValidator() =>
        BuildValidatorWithCatalog(new Dictionary<string, string>
        {
            { "CIR", "CIRCUITO" },
            { "CHI", "CHICHI SUAREZ" },
            { "CAN", "CANCUN" },
            { "CON", "CONKAL" },
        });

    private static RegistrarCotizacionEdiValidator BuildValidatorWithCatalog(
        Dictionary<string, string> sucursales)
    {
        var options = new IntegracionesAwOptions { Sucursales = sucursales };
        var snapshot = new FakeOptionsSnapshot<IntegracionesAwOptions>(options);
        return new RegistrarCotizacionEdiValidator(snapshot);
    }

    private static RegistrarCotizacionEdiCommand BuildCommand(string sucursal) =>
        new(
            QuoteReference: "Q-2026-00001",
            Sucursal: sucursal,
            EdiContent: new string('x', 200) + "#END#",
            CustomerTaxId: "TIG890101AAA",
            CustomerName: "Tiglass",
            Source: "glass_agent",
            ItemsCount: 1,
            PayloadOriginalJson: "{}");

    private sealed class FakeOptionsSnapshot<T> : IOptionsSnapshot<T> where T : class, new()
    {
        public FakeOptionsSnapshot(T value) { Value = value; }
        public T Value { get; }
        public T Get(string? name) => Value;
    }
}
