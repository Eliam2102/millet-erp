using Millet.Tesoreria.Application.Movimientos;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.UnitTests.Movimientos;

/// <summary>La cuenta destino de un movimiento es PII: completa solo con `tesoreria.movimientos.ver-cuenta-completa`.</summary>
public sealed class MovimientoCuentaDestinoTests
{
    private const string Clabe = "032180000118359719";

    private static MovimientoBancario ConDestino()
    {
        var empresa = Guid.NewGuid();
        var cuenta = new CuentaBancaria(empresa, "BBVA", "1234567890", null, "MXN");
        var m = MovimientoBancario.RegistrarIngreso(empresa, cuenta, 1_000m, new DateOnly(2026, 10, 18), null, null, null, null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        typeof(MovimientoBancario).GetProperty(nameof(MovimientoBancario.DestinoNumeroCuenta))!.SetValue(m, Clabe);
        return m;
    }

    [Fact]
    public void Sin_permiso_la_cuenta_destino_sale_enmascarada()
    {
        var r = MovimientoBancarioMapper.ToResponse(ConDestino());
        Assert.NotEqual(Clabe, r.DestinoNumeroCuenta);
        Assert.EndsWith(Clabe[^4..], r.DestinoNumeroCuenta);
    }

    [Fact]
    public void Con_permiso_la_cuenta_destino_sale_completa()
    {
        var r = MovimientoBancarioMapper.ToResponse(ConDestino(), verCuentaCompleta: true);
        Assert.Equal(Clabe, r.DestinoNumeroCuenta);
    }
}
