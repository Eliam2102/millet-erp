using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// B8 en los pagos de Tesorería: la PP del pago a proveedor (directo, corrida o SPEI de layout; todos publican el mismo evento) y el
/// movimiento del extracto abonan a la cuenta contable de la cuenta bancaria de Millet que manda Tesorería, no a la cuenta única del
/// rol BANCOS. Sin ese dato (cuenta bancaria sin cuenta contable o evento anterior) se conserva el rol. Datos FIX.
/// </summary>
public class B8BancoRealPolizaPagoProveedorTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    private static EventoContable PagoProveedor(string? cuentaContableRef, bool anticipoAgente = false) =>
        N.Normalizar(N.PagoProveedorAplicado, Guid.NewGuid(), JsonSerializer.Serialize(new
        {
            EmpresaId = Empresa, OcurridoEn = DateTimeOffset.UtcNow, FacturaProveedorId = Guid.NewGuid(), PagoId = Guid.NewGuid(),
            Monto = 1_000m, Moneda = "MXN", FechaPago = new DateOnly(2026, 11, 14), MetodoPago = "PUE", ReferenciaBancaria = "SPEI-FIX",
            CuentaBancariaId = Guid.NewGuid(), EsAnticipoAgente = anticipoAgente, ProveedorId = Guid.NewGuid(), SucursalId = Guid.NewGuid(),
            CuentaContableRef = cuentaContableRef,
        }));

    private static EventoContable Movimiento(string? cuentaContableRef) =>
        N.Normalizar(N.MovimientoBancarioRegistrado, Guid.NewGuid(), JsonSerializer.Serialize(new
        {
            EmpresaId = Empresa, MovimientoBancarioId = Guid.NewGuid(), Sentido = 2, Monto = 100m, Moneda = "MXN",
            FechaValor = new DateOnly(2026, 11, 14), ReferenciaBancaria = "COM-FIX", ConceptoNombre = "Comisiones", Iva = 16m,
            CuentaContableRef = cuentaContableRef,
        }));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void El_pago_a_proveedor_abona_a_la_cuenta_contable_de_su_cuenta_bancaria(bool anticipoAgente) =>
        PagoProveedor(" 102.01.01.01 ", anticipoAgente).CuentaPorRol.Should()
            .BeEquivalentTo(new Dictionary<string, string> { ["BANCOS"] = "102.01.01.01" });

    [Fact]
    public void El_movimiento_del_extracto_va_a_la_cuenta_contable_de_su_cuenta_bancaria() =>
        Movimiento("102.01.01.01").CuentaPorRol.Should().BeEquivalentTo(new Dictionary<string, string> { ["BANCOS"] = "102.01.01.01" });

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Sin_cuenta_contable_en_la_cuenta_bancaria_se_usa_el_rol(string? cuentaContableRef)
    {
        PagoProveedor(cuentaContableRef).CuentaPorRol.Should().BeNull();
        Movimiento(cuentaContableRef).CuentaPorRol.Should().BeNull();
    }
}
