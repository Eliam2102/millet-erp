using Millet.CuentasPorPagar.Domain.AnticipoProveedor;
using Anticipo = Millet.CuentasPorPagar.Domain.AnticipoProveedor.AnticipoProveedor;
namespace Millet.CuentasPorPagar.UnitTests;
public sealed class AnticipoGastoCeTests
{
    [Fact]
    public void Corregir_el_gasto_no_deja_otro_saldo_amortizable_en_cxp()
    {
        var ahora = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var a = Anticipo.DesdeGastoComercioExterior(Guid.NewGuid(), Guid.NewGuid(), 47000,
            new(2026, 11, 5), "CE-P23", $"{Guid.NewGuid()}:gasto:{Guid.NewGuid()}", ahora);
        a.Amortizar(47000, ahora); Assert.Equal(0, a.SaldoAmortizable);
        a.ReabrirAplicacionCe(47000); a.Cancelar("Corrección", ahora);
        Assert.Equal(EstadoAnticipo.Cancelado, a.Estado); Assert.Equal(0, a.SaldoAmortizable);
        a.ReabrirAplicacionCe(47000); a.Amortizar(47000, ahora);
        Assert.Null(a.FechaCancelacion); Assert.Equal(0, a.SaldoAmortizable);
    }
}
