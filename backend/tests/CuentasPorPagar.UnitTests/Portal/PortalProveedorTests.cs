using Millet.CuentasPorPagar.Application.Portal;
using Millet.CuentasPorPagar.Domain.Portal;
using Millet.SharedKernel.Application.Exceptions;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.CuentasPorPagar.UnitTests.Portal;

public sealed class PortalProveedorTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Codigo_se_guarda_con_hash_y_acepta_el_correcto_una_sola_vez()
    {
        var c = new CodigoPortal(Guid.NewGuid(), Guid.NewGuid(), "123456", Ahora);

        c.CodigoHash.Should().NotContain("123456");
        c.Verificar("123456", Ahora.AddMinutes(1)).Should().BeTrue();
        c.Verificar("123456", Ahora.AddMinutes(2)).Should().BeFalse("ya fue consumido");
    }

    [Fact]
    public void Codigo_vence_a_los_10_minutos()
    {
        var c = new CodigoPortal(Guid.NewGuid(), Guid.NewGuid(), "123456", Ahora);

        c.Verificar("123456", Ahora.AddMinutes(10)).Should().BeFalse();
    }

    [Fact]
    public void Cinco_intentos_fallidos_agotan_el_codigo_aunque_el_siguiente_sea_correcto()
    {
        var c = new CodigoPortal(Guid.NewGuid(), Guid.NewGuid(), "123456", Ahora);

        for (var i = 0; i < CodigoPortal.MaxIntentos; i++) c.Verificar("000000", Ahora).Should().BeFalse();

        c.Verificar("123456", Ahora).Should().BeFalse();
        c.Intentos.Should().Be(CodigoPortal.MaxIntentos);
    }

    [Theory]
    [InlineData(" Ventas@Prov.MX ", "ventas@prov.mx")]
    public void Acceso_normaliza_el_correo(string entrada, string esperado) =>
        new AccesoPortalProveedor(Guid.NewGuid(), Guid.NewGuid(), entrada).Email.Should().Be(esperado);

    [Theory]
    [InlineData("sin-arroba")]
    [InlineData("a@b")]
    [InlineData("dos@@x.com")]
    [InlineData("con espacio@x.com")]
    public void Acceso_rechaza_correos_invalidos(string correo)
    {
        var crear = () => new AccesoPortalProveedor(Guid.NewGuid(), Guid.NewGuid(), correo);

        crear.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("PORTAL_CORREO_INVALIDO");
    }

    private static Factura Nueva() => Factura.CapturarSinOc(Guid.NewGuid(), null, "UUID-1", Guid.NewGuid(), Guid.NewGuid(), "10", "A",
        Ahora, Ahora, DateOnly.FromDateTime(Ahora.UtcDateTime), "MXN", null, 100, 0, 16, 0, 116, "prueba", Ahora);

    [Fact]
    public void Factura_con_FALTA_REPP_se_muestra_detenida_con_el_motivo_en_palabras_del_proveedor()
    {
        var f = Nueva();
        f.EnviarARevision(Factura.MotivoFaltaReppId, Guid.NewGuid(), null, Ahora);

        var r = PortalMapeo.Mapear(f);

        r.Detenida.Should().BeTrue();
        r.Estado.Should().Contain("falta el complemento de pago");
        r.Motivo.Should().Be("Falta el complemento de pago de la factura A-10");
    }

    [Fact]
    public void Factura_recibida_no_esta_detenida_y_no_muestra_fecha_de_pago()
    {
        var r = PortalMapeo.Mapear(Nueva());

        r.Detenida.Should().BeFalse();
        r.Estado.Should().Be("Recibida");
        r.FechaProgramadaPago.Should().BeNull();
        r.Folio.Should().Be("A-10");
    }
}
