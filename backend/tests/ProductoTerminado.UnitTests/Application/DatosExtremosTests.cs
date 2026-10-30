using Microsoft.EntityFrameworkCore;

namespace Millet.ProductoTerminado.UnitTests.Application;

/// <summary>M-27: datos extremos del renglón de origen.</summary>
public sealed class DatosExtremosTests
{
    [Fact]
    public async Task M27_Area_con_emoji_y_script_se_guarda_tal_cual()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 4);
        const string area = "Templado 🔥 <script>alert('x')</script> áéñ";
        var r = await t.Handler.Handle(new Millet.ProductoTerminado.Application.Escaneos.RegistrarEscaneoPiezaCommand(
            EntornoPt.Empresa, "Demo", "E1", "P1", 1, null, "1", 2821, area,
            new DateTime(2026, 10, 28, 8, 0, 0, DateTimeKind.Unspecified), "01"), CancellationToken.None);
        r.Resultado.Should().Be("Alta");
        (await t.Db.Escaneos.SingleAsync()).Area.Should().Be(area);
    }

    [Fact]
    public async Task M27_Pedido_con_ceros_a_la_izquierda_y_espacios_se_busca_recortado_y_con_los_ceros()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("00123", 1, 4);
        var r = await t.Escanear("E1", pedido: "  00123 ");
        r.Resultado.Should().Be("Alta");
        (await t.Db.Escaneos.SingleAsync()).NumeroPedido.Should().Be("00123");
        // Los ceros son parte del número: "123" es otro pedido.
        (await t.Escanear("E2", pedido: "123")).Resultado.Should().Be("Pospuesto");
    }
}
