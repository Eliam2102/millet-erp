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

        var r = PortalMapeo.Mapear(f, null);

        r.Detenida.Should().BeTrue();
        r.Estado.Should().Contain("falta el complemento de pago");
        r.Motivo.Should().Be("Falta el complemento de pago de la factura A-10");
    }

    [Fact]
    public void Factura_recibida_no_esta_detenida_y_no_muestra_fecha_de_pago()
    {
        var r = PortalMapeo.Mapear(Nueva(), new DateOnly(2026, 11, 5));

        r.Detenida.Should().BeFalse();
        r.Estado.Should().Be("Recibida");
        r.FechaProgramadaPago.Should().BeNull("solo una factura autorizada está programada");
        r.Folio.Should().Be("A-10");
    }

    [Fact]
    public void Factura_autorizada_muestra_la_fecha_de_la_corrida_y_deja_el_vencimiento_aparte()
    {
        var f = Nueva();
        f.Autorizar(null, Ahora);
        var corrida = new DateOnly(2026, 11, 5);

        var r = PortalMapeo.Mapear(f, corrida);

        r.Estado.Should().Be("Programada para pago");
        r.FechaProgramadaPago.Should().Be(corrida);
        r.FechaVencimiento.Should().Be(f.FechaVencimiento).And.NotBe(corrida);
    }

    [Fact]
    public void Factura_autorizada_sin_corrida_queda_sin_fecha_programada()
    {
        var f = Nueva();
        f.Autorizar(null, Ahora);

        PortalMapeo.Mapear(f, null).FechaProgramadaPago.Should().BeNull();
    }

    [Fact]
    public void CA8_4_factura_de_proveedor_sin_opinion_vigente_se_muestra_detenida_por_documentacion_y_sin_fecha_programada()
    {
        var f = Nueva();
        f.Autorizar(null, Ahora);

        var r = PortalMapeo.Mapear(f, new DateOnly(2026, 11, 5), sinOpinionCumplimiento: true);

        r.Detenida.Should().BeTrue();
        r.Estado.Should().Be("Detenida: documentación");
        r.Motivo.Should().Be("Falta tu opinión de cumplimiento vigente");
        r.FechaProgramadaPago.Should().BeNull();
        PortalMapeo.Mapear(f, new DateOnly(2026, 11, 5), sinOpinionCumplimiento: false).Detenida.Should().BeFalse("con la opinión vigente se libera sola");
    }

    [Fact]
    public void Factura_ya_detenida_por_falta_de_repp_conserva_su_motivo_aunque_falte_la_opinion()
    {
        var f = Nueva();
        f.EnviarARevision(Factura.MotivoFaltaReppId, Guid.NewGuid(), null, Ahora);

        PortalMapeo.Mapear(f, null, sinOpinionCumplimiento: true).Estado.Should().Contain("falta el complemento de pago");
    }
}
