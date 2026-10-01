using System.Text.Json;
using System.Text.RegularExpressions;

namespace Millet.Integraciones.Aw.UnitTests.Clientes;

/// <summary>Forma de los fixtures sinteticos de ADM-06 (sincronizacion de clientes A+W).</summary>
public sealed class ClientesFixturesTests
{
    private static readonly string[] Escenarios =
    [
        "alta-valida", "repeticion", "cambio-comercial", "correccion-fiscal-local",
        "moneda-desconocida", "condicion-sin-coincidencia-o-duplicada", "cambio-catalogo-dias",
        "estado-desconocido", "baja-explicita-demo", "fallo-y-reintento",
        "rfc-generico-dos-referencias", "competencia-pedido-sincronizacion",
    ];

    private static readonly string[] ColumnasKunden =
    [
        "ID", "MANDANT", "NAME1", "NAME2", "NAME3", "STRASSE", "ORT", "PLZ", "PROVINZ", "LAND",
        "UST_ID", "STEUERNUMMER", "TLF1", "TLF2", "MAIL", "ZAHLBED", "WAEHRUNG", "KREDIT_LIMIT",
        "KREDIT_LIMIT1", "KREDIT_LIMIT_NET", "KZ_STATUS", "KZ_GESPERRT", "DATUM", "TRANSACTION_TIME",
    ];

    private static JsonElement Cargar(string escenario)
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Clientes", "Fixtures", escenario + ".json");
        return JsonDocument.Parse(File.ReadAllText(ruta)).RootElement;
    }

    public static TheoryData<string> Todos() => new(Escenarios);

    [Theory]
    [MemberData(nameof(Todos))]
    public void Fixture_tiene_forma_valida(string escenario)
    {
        var raiz = Cargar(escenario);

        raiz.GetProperty("escenario").GetString().Should().Be(escenario);
        raiz.GetProperty("descripcion").GetString().Should().NotBeNullOrWhiteSpace();
        raiz.TryGetProperty("estadoErpPrevio", out _).Should().BeTrue();
        var esperado = raiz.GetProperty("resultadoEsperado");
        esperado.ValueKind.Should().Be(JsonValueKind.Object);
        esperado.EnumerateObject().Should().NotBeEmpty();

        var origen = raiz.GetProperty("origen");
        var clientes = origen.GetProperty("KU_KUNDEN").EnumerateArray().ToList();
        clientes.Should().NotBeEmpty();
        foreach (var c in clientes)
            foreach (var col in ColumnasKunden)
                c.TryGetProperty(col, out _).Should().BeTrue($"{escenario}: falta {col}");
        foreach (var z in origen.GetProperty("KA_ZAHLBED").EnumerateArray())
            foreach (var col in new[] { "BEZ", "NUMMER", "BRUTTOTAGE" })
                z.TryGetProperty(col, out _).Should().BeTrue($"{escenario}: falta {col}");
    }

    [Theory]
    [MemberData(nameof(Todos))]
    public void Fixture_no_contiene_rfc_real(string escenario)
    {
        var rfc = new Regex("^[A-ZÑ&]{3,4}[0-9]{6}[A-Z0-9]{3}$");
        foreach (var c in Cargar(escenario).GetProperty("origen").GetProperty("KU_KUNDEN").EnumerateArray())
        {
            var v = c.GetProperty("STEUERNUMMER").GetString();
            if (v is null) continue;
            (v.StartsWith("DEMO", StringComparison.Ordinal) || v is "XAXX010101000" or "XEXX010101000" || !rfc.IsMatch(v))
                .Should().BeTrue($"{escenario}: RFC no sintetico {v}");
        }
    }

    [Fact]
    public void Existen_los_12_escenarios()
    {
        Escenarios.Should().HaveCount(12);
        foreach (var e in Escenarios) Cargar(e);
    }
}
