using System.Text.Json;
using Millet.Api.Endpoints.Almacen;

namespace Millet.Api.UnitTests.Almacen;

/// <summary>v9: SALIDA_MANUAL con origen CANCELACION_ENTRADA se reconoce para revertir la recepción del ERP; cualquier otra no.</summary>
public sealed class CancelacionEntradaAppTests
{
    private static JsonElement Datos(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Origen_CANCELACION_ENTRADA_da_la_entrada_y_el_motivo()
    {
        var datos = Datos("""{"origen":{"tipo":"CANCELACION_ENTRADA","entrada":"EN-000820","compra":"OC-5521","encargoAw":36008200,"motivo":"Se capturó dos veces"}}""");

        Assert.Equal(("EN-000820", "Se capturó dos veces"), AlmacenesAppSalida.CancelacionEntrada(datos, "SALIDA_MANUAL"));
    }

    [Theory]
    [InlineData("""{"origen":{"tipo":"CANCELACION_ENTRADA","entrada":"EN-1"}}""", "AJUSTE_SALIDA")]
    [InlineData("""{"origen":{"tipo":"DEVOLUCION_VENTA","salidaOriginal":"MV-1"}}""", "SALIDA_MANUAL")]
    [InlineData("""{"origen":{"tipo":"CANCELACION_ENTRADA"}}""", "SALIDA_MANUAL")]
    [InlineData("""{"origen":"CORTE"}""", "SALIDA_MANUAL")]
    [InlineData("""{}""", "SALIDA_MANUAL")]
    public void Lo_que_no_es_cancelacion_de_entrada_sigue_como_salida_normal(string json, string tipo) =>
        Assert.Null(AlmacenesAppSalida.CancelacionEntrada(Datos(json), tipo));
}
