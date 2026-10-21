using Millet.Almacen.Domain.Saldos;
namespace Millet.Almacen.UnitTests;
public sealed class SaldoCostoExpedienteTests
{
    [Fact]
    public void Gasto_tardio_solo_revalua_existencia_sin_mover_cantidad()
    {
        var saldo = new SaldoInventario(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 400, 100);
        var reparto = SaldoInventario.SepararCosto(10000, 1000, 400);
        saldo.AjustarValor(reparto.Inventario, DateTimeOffset.UtcNow);
        Assert.Equal(4000, reparto.Inventario); Assert.Equal(6000, reparto.CostoVentas);
        Assert.Equal(400, saldo.Cantidad); Assert.Equal(400, saldo.CantidadDisponible);
        Assert.Equal(44000, saldo.ValorInventarioMxn); Assert.Equal(110, saldo.CostoPromedioMxn);
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
