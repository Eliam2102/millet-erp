using Millet.Almacen.Domain.Saldos;
namespace Millet.Almacen.UnitTests;
public sealed class SaldoCostoExpedienteTests
{
    [Fact]
    public void Reparto_por_ubicacion_revalua_sin_mover_cantidad()
    {
        var saldo = new SaldoInventario(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 400, 100);
        var reparto = SaldoInventario.SepararCosto(10000, 1000, 400);
        saldo.AjustarValor(reparto.Inventario, DateTimeOffset.UtcNow);
        Assert.Equal(4000, reparto.Inventario); Assert.Equal(6000, reparto.CostoVentas);
        Assert.Equal(400, saldo.Cantidad); Assert.Equal(400, saldo.CantidadDisponible);
        Assert.Equal(44000, saldo.ValorInventarioMxn); Assert.Equal(110, saldo.CostoPromedioMxn);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Costo_aplicado_json_historico_conserva_gasto_de_importacion(bool web)
    {
        var opciones = web ? System.Text.Json.JsonSerializerOptions.Web : System.Text.Json.JsonSerializerOptions.Default;
        var anterior = web ? "costoVentasMxn" : "CostoVentasMxn";
        var actual = web ? "gastoImportacionMxn" : "GastoImportacionMxn";
        var historico = System.Text.Json.JsonSerializer.Deserialize<Millet.Almacen.Application.EventListeners.CostoPartidaAplicado>(
            "{\"" + anterior + "\":6000}", opciones)!;
        Assert.Equal(6000, historico.GastoImportacionMxn);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(historico, opciones);
        Assert.Equal(6000, json.GetProperty(anterior).GetDecimal());
        Assert.Equal(6000, json.GetProperty(actual).GetDecimal());
        Assert.Equal(historico.GastoImportacionMxn,
            System.Text.Json.JsonSerializer.Deserialize<Millet.Almacen.Application.EventListeners.CostoPartidaAplicado>(json, opciones)!.GastoImportacionMxn);
    }
    [Fact]
    public void Correccion_negativa_revierte_solo_la_diferencia_y_conserva_centavos()
    {
        var saldo = new SaldoInventario(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, 100);
        saldo.AjustarValor(100, DateTimeOffset.UtcNow); Assert.Equal(400, saldo.ValorInventarioMxn);
        saldo.AjustarValor(-10, DateTimeOffset.UtcNow); Assert.Equal(390, saldo.ValorInventarioMxn);
    }
    [Fact]
    public void Sin_existencia_todo_va_a_consumo() => Assert.Equal((0m, 10000m), SaldoInventario.SepararCosto(10000, 1000, 0));
}
