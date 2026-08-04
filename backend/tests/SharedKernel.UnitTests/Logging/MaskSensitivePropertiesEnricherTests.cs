using Millet.SharedKernel.Infrastructure.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace Millet.SharedKernel.UnitTests.Logging;

public class MaskSensitivePropertiesEnricherTests
{
    [Theory]
    [InlineData("password", "MyP@ss123", "My*****23")]
    [InlineData("token", "ABCDEF1234567890", "AB************90")]
    [InlineData("curp", "ABCD010101HDFGRL01", "AB**************01")]
    [InlineData("clabe", "012345678901234567", "01**************67")]
    [InlineData("apellidoPaterno", "García", "Ga**ía")]
    public void Should_MaskValue_When_PropertyIsSensitive(string key, string value, string expected)
    {
        var enricher = new MaskSensitivePropertiesEnricher();
        var logEvent = CreateLogEvent(key, value);

        enricher.Enrich(logEvent, new LogEventPropertyFactory());

        var prop = logEvent.Properties[key].ToString().Trim('"');
        prop.Should().Be(expected);
    }

    [Theory]
    [InlineData("rfc", "XAXX010101000")]
    [InlineData("razonSocial", "Vidrios de Millet SA de CV")]
    [InlineData("domicilioFiscal", "Av Reforma 100")]
    [InlineData("nombreComercial", "Millet Glass")]
    [InlineData("entidad", "Cliente")]
    public void Should_LeaveUnchanged_When_PropertyIsNotSensitive(string key, string value)
    {
        var enricher = new MaskSensitivePropertiesEnricher();
        var logEvent = CreateLogEvent(key, value);

        enricher.Enrich(logEvent, new LogEventPropertyFactory());

        var prop = logEvent.Properties[key].ToString().Trim('"');
        prop.Should().Be(value);
    }

    [Fact]
    public void Should_BeCaseInsensitive_OnPropertyKeyMatching()
    {
        var enricher = new MaskSensitivePropertiesEnricher();
        var logEvent = CreateLogEvent("PASSWORD", "secret123");

        enricher.Enrich(logEvent, new LogEventPropertyFactory());

        logEvent.Properties["PASSWORD"].ToString().Trim('"').Should().NotBe("secret123");
    }

    [Theory]
    [InlineData("ab", "**")]
    [InlineData("abcd", "****")]
    public void Should_MaskShortValuesCompletely_When_LengthLessOrEqualTo4(string input, string expected)
    {
        // Para valores ≤ 4 chars, todo se enmascara (no quedaría ninguna info útil
        // dejando 2 visibles y 0 ocultos).
        MaskSensitivePropertiesEnricher.Mask(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("abcde", "ab*de")]
    [InlineData("abcdefghij", "ab******ij")]
    public void Should_KeepFirstAndLast2Visible_When_LengthGreaterThan4(string input, string expected)
    {
        MaskSensitivePropertiesEnricher.Mask(input).Should().Be(expected);
    }

    private static LogEvent CreateLogEvent(string propertyName, string propertyValue)
    {
        var template = new MessageTemplate(
            "Test {" + propertyName + "}",
            new[] { new PropertyToken(propertyName, "{" + propertyName + "}") });

        var properties = new[]
        {
            new LogEventProperty(propertyName, new ScalarValue(propertyValue)),
        };

        return new LogEvent(
            DateTimeOffset.UnixEpoch,
            LogEventLevel.Information,
            exception: null,
            template,
            properties);
    }

    /// <summary>Implementación mínima de ILogEventPropertyFactory para tests.</summary>
    private sealed class LogEventPropertyFactory : Serilog.Core.ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
        {
            return new LogEventProperty(name, new ScalarValue(value));
        }
    }
}
