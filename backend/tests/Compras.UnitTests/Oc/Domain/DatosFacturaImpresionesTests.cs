using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

public sealed class DatosFacturaImpresionesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static OrdenCompra Crear() => new(Guid.NewGuid(), Guid.NewGuid(),
        Folio.Parse("OC-MID2026-000123"), 2026, Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 20),
        sinRequisicionPrevia: true, motivoSinRequisicion: "Prueba ficticia");

    [Theory]
    [InlineData(null, "PPD", "99", "uso de CFDI")]
    [InlineData("G03", null, "99", "método de pago")]
    [InlineData("G03", "PPD", null, "forma de pago")]
    [InlineData(null, null, null, "uso de CFDI, método de pago, forma de pago")]
    public void Envio_exige_datos_y_nombra_faltantes(string? uso, string? metodo, string? forma, string faltantes)
    {
        var oc = Crear();
        oc.ActualizarDatosFactura(uso, metodo, forma);
        oc.AgregarLineaManual(Guid.NewGuid(), Guid.NewGuid(), 1, "PZA", 10, Guid.NewGuid());
        var error = Assert.Throws<BusinessRuleException>(() => oc.EnviarAAutorizacion(Ahora));
        Assert.Equal("OC_DATOS_FACTURA_INCOMPLETOS", error.Code);
        Assert.Contains(faltantes, error.Message);
        Assert.Equal(EstadoOrdenCompra.Borrador, oc.Estado);
    }

    [Fact]
    public void Metodo_invalido_no_modifica_datos()
    {
        var oc = Crear();
        var error = Assert.Throws<BusinessRuleException>(() => oc.ActualizarDatosFactura("G03", "XXX", "99"));
        Assert.Equal("OC_METODO_PAGO_INVALIDO", error.Code);
        Assert.Null(oc.UsoCfdiClave);
    }

    [Fact]
    public void Borrador_admite_vacios_y_patch_conserva_o_limpia()
    {
        var oc = Crear();
        Assert.Null(oc.MetodoPago);
        oc.ActualizarDatosFactura("G03", "PPD", "99");
        oc.ActualizarDatosFactura(null, null, "03");
        Assert.Equal("G03", oc.UsoCfdiClave);
        Assert.Equal("PPD", oc.MetodoPago);
        Assert.Equal("03", oc.FormaPagoClave);
        oc.ActualizarDatosFactura("", "", "");
        Assert.Null(oc.UsoCfdiClave);
        Assert.Null(oc.MetodoPago);
        Assert.Null(oc.FormaPagoClave);
    }

    [Fact]
    public void Datos_se_congelan_al_enviar()
    {
        var oc = Crear();
        oc.ActualizarDatosFactura("G03", "PPD", "99");
        oc.AgregarLineaManual(Guid.NewGuid(), Guid.NewGuid(), 1, "PZA", 10, Guid.NewGuid());
        oc.EnviarAAutorizacion(Ahora);
        Assert.Throws<BusinessRuleException>(() => oc.ActualizarDatosFactura("G01", "PUE", "03"));
    }

    [Fact]
    public void Impresiones_son_secuenciales_y_reemplazo_reinicia()
    {
        var ocId = Guid.NewGuid();
        OrdenCompraPdf CrearPdf() => new(Guid.NewGuid(), ocId, "ficticio.pdf", "application/pdf", 10, Ahora, "Prueba");
        var pdf = CrearPdf();
        var usuario = Guid.NewGuid();
        Assert.Equal(0, pdf.Impresiones);
        Assert.Null(pdf.UltimaImpresionEn);
        Assert.Null(pdf.UltimaImpresionPor);
        Assert.Equal(1, pdf.RegistrarImpresion(usuario, Ahora));
        Assert.Equal(2, pdf.RegistrarImpresion(usuario, Ahora.AddMinutes(1)));
        Assert.Equal(3, pdf.RegistrarImpresion(usuario, Ahora.AddMinutes(2)));
        Assert.Equal(usuario, pdf.UltimaImpresionPor);
        Assert.Equal(Ahora.AddMinutes(2), pdf.UltimaImpresionEn);
        Assert.Equal(1, CrearPdf().RegistrarImpresion(usuario, Ahora.AddMinutes(3)));
    }
}
