using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// B8 (prueba Sprints 3-4): la póliza del pago de cliente (#12) cargaba a la cuenta única del rol BANCOS, así que un cobro en USD a
/// una cuenta en dólares quedaba en la cuenta de bancos MXN. Ahora carga a la cuenta contable de la cuenta bancaria del depósito, que
/// Tesorería manda en el evento; sin ese dato se conserva el rol. Datos FIX.
/// </summary>
public class B8BancoRealPolizaCobroTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Cliente = Guid.NewGuid();

    private static EventoContable Pago(string? cuentaContableRef) =>
        N.Normalizar(N.PagoClienteConfirmado, Guid.NewGuid(), JsonSerializer.Serialize(new
        {
            EmpresaId = Empresa, OcurridoEn = DateTimeOffset.UtcNow, PropuestaId = Guid.NewGuid(), ClienteId = Cliente,
            MovimientoBancarioId = Guid.NewGuid(), CuentaBancariaId = Guid.NewGuid(), Monto = 10_000m, Moneda = "USD",
            FechaValor = new DateOnly(2026, 11, 5), Referencia = "WIRE-FIX", TipoCambioPago = 18.7592m,
            CuentaContableRef = cuentaContableRef,
            Facturas = new[] { new { FacturaVentaId = Guid.NewGuid(), ImporteAplicado = 10_000m } },
        }));

    [Fact]
    public void El_pago_lleva_la_cuenta_contable_de_su_cuenta_bancaria_para_el_rol_bancos() =>
        Pago(" 102.02.01.01 ").CuentaPorRol.Should().BeEquivalentTo(new Dictionary<string, string> { ["BANCOS"] = "102.02.01.01" });

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Sin_cuenta_contable_en_la_cuenta_bancaria_se_usa_el_rol(string? cuentaContableRef) =>
        Pago(cuentaContableRef).CuentaPorRol.Should().BeNull();

    [Fact]
    public void El_motor_resuelve_el_codigo_a_la_cuenta_del_catalogo()
    {
        var usd = Guid.NewGuid();
        var propias = GenerarPolizaDesdeEventoHandler.CuentasPropias(Pago("102.02.01.01").CuentaPorRol!,
            new Dictionary<string, Guid> { ["102.01.01.01"] = Guid.NewGuid(), ["102.02.01.01"] = usd });

        propias.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, Guid>("BANCOS", usd));
    }

    [Fact]
    public void Un_codigo_que_no_esta_en_el_catalogo_se_rechaza_y_no_cae_en_la_cuenta_del_rol()
    {
        var act = () => GenerarPolizaDesdeEventoHandler.CuentasPropias(Pago("102.99").CuentaPorRol!, new Dictionary<string, Guid>());

        act.Should().Throw<RechazoMotorException>()
            .Which.Should().Match<RechazoMotorException>(r => r.Causa == CausaRechazoContable.CuentaNoValida && r.Detalle == "102.99");
    }
}
