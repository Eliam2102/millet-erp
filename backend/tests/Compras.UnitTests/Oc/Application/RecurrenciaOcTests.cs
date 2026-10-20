using Millet.Compras.Application.Oc;

namespace Millet.Compras.UnitTests.Oc.Application;

public class RecurrenciaOcTests
{
    private static readonly Guid Proveedor = Guid.CreateVersion7();
    private static readonly Guid Comprador = Guid.CreateVersion7();

    [Fact]
    public void EsRecurrente_con_tres_ocs_es_true_y_con_dos_es_false()
    {
        Assert.True(RecurrenciaOc.EsRecurrente([new(Proveedor, Comprador, 3)], Proveedor, Comprador));
        Assert.False(RecurrenciaOc.EsRecurrente([new(Proveedor, Comprador, 2)], Proveedor, Comprador));
    }

    [Fact]
    public void EsRecurrente_ignora_conteos_de_otro_comprador_u_otro_proveedor()
    {
        var conteos = new[]
        {
            new ConteoRecurrenciaOc(Proveedor, Guid.CreateVersion7(), 5),
            new ConteoRecurrenciaOc(Guid.CreateVersion7(), Comprador, 5),
        };
        Assert.False(RecurrenciaOc.EsRecurrente(conteos, Proveedor, Comprador));
    }

    [Fact]
    public void Desde_resta_la_ventana_de_60_dias()
    {
        var ahora = new DateTimeOffset(2026, 10, 20, 23, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 8, 21), RecurrenciaOc.Desde(ahora));
    }
}
