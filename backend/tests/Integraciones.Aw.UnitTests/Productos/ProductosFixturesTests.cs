using System.Text.Json;

namespace Millet.Integraciones.Aw.UnitTests.Productos;

/// <summary>Forma de los fixtures sinteticos de ADM-07 (sincronizacion de productos A+W). No prueba integracion real.</summary>
public sealed class ProductosFixturesTests
{
    private static readonly string[] Escenarios =
    [
        "nominal", "dos-medidas-mismo-codigo", "tratado-templado", "tratado-laminado",
        "unidad-desconocida", "duplicado", "baja", "baja-con-historico",
        "conflicto-version", "campo-fiscal-ya-completado", "medida-nula-no-es-cero",
    ];

    private static readonly string[] ColumnasArticulo =
        ["producto_ref", "descripcion", "unidad_medida", "baja", "variantes", "TRANSACTION_TIME"];

    private static readonly string[] CamposVariante =
        ["clave_variante", "alto_mm", "ancho_mm", "espesor_mm", "composicion"];

    private static JsonElement Cargar(string escenario)
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Productos", "Fixtures", escenario + ".json");
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
        raiz.GetProperty("resultadoEsperado").EnumerateObject().Should().NotBeEmpty();

        var articulos = raiz.GetProperty("origen").GetProperty("vw_erp_articulo").EnumerateArray().ToList();
        articulos.Should().NotBeEmpty();
        foreach (var a in articulos)
        {
            foreach (var col in ColumnasArticulo)
                a.TryGetProperty(col, out _).Should().BeTrue($"{escenario}: falta {col}");
            a.GetProperty("producto_ref").GetString()!.Length.Should().BeInRange(1, 50);
            a.GetProperty("descripcion").GetString()!.Length.Should().BeInRange(1, 254);
            a.GetProperty("producto_ref").GetString().Should().StartWith("DEMO");
            foreach (var v in a.GetProperty("variantes").EnumerateArray())
                foreach (var campo in CamposVariante)
                    v.TryGetProperty(campo, out _).Should().BeTrue($"{escenario}: variante sin {campo}");
        }
    }

    [Fact]
    public void Existen_los_escenarios_del_plan()
    {
        Escenarios.Should().HaveCountGreaterThanOrEqualTo(10);
        foreach (var e in Escenarios) Cargar(e);
    }
}
