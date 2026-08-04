using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.UnitTests.Domain;

public class ClienteTests
{
    private static Cliente Crear(
        string clave = "CLI-1",
        string razonSocial = "Global Construcciones SA de CV",
        OrigenMaster origen = OrigenMaster.Manual,
        string? referenciaExterna = null,
        string? rfc = null,
        string? regimenFiscal = null,
        string? codigoPostalFiscal = null,
        string? metodoPagoDefault = null,
        string monedaDefault = "MXN") =>
        new(
            id: Guid.CreateVersion7(),
            clave: clave,
            razonSocial: razonSocial,
            origen: origen,
            referenciaExterna: referenciaExterna,
            rfc: rfc,
            regimenFiscal: regimenFiscal,
            codigoPostalFiscal: codigoPostalFiscal,
            metodoPagoDefault: metodoPagoDefault,
            monedaDefault: monedaDefault);

    [Fact]
    public void Should_Create_ConFiscalesNulos_SinBloquear()
    {
        // Regla ADR-0048/levantamiento §5.1: fiscales incompletos NO bloquean el alta.
        var c = Crear();
        Assert.Equal(EstatusCatalogo.Activo, c.Estatus);
        Assert.False(c.DatosFiscalesCompletos);
    }

    [Fact]
    public void Should_ReportarFiscalesCompletos_ConRfcRegimenYCp()
    {
        var c = Crear(rfc: "GCO123456AB9", regimenFiscal: "601", codigoPostalFiscal: "97300");
        Assert.True(c.DatosFiscalesCompletos);
    }

    [Theory]
    [InlineData("")]
    [InlineData("THIS-CLAVE-EXCEEDS-20-CHARS")]
    public void Should_Throw_When_ClaveInvalida(string clave)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(clave: clave));
        Assert.Equal("CLIENTE_CLAVE_INVALIDA", ex.Code);
    }

    [Theory]
    [InlineData("RFC123")]
    [InlineData("RFC12345678901234")]
    public void Should_Throw_When_RfcLongitudInvalida(string rfc)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(rfc: rfc));
        Assert.Equal("CLIENTE_RFC_INVALIDO", ex.Code);
    }

    [Fact]
    public void Should_Throw_When_MetodoPagoInvalido()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(metodoPagoDefault: "PIP"));
        Assert.Equal("CLIENTE_METODO_PAGO_INVALIDO", ex.Code);
    }

    [Fact]
    public void ActualizarDatos_CompletaFiscales_YLimpiaConFlags()
    {
        var c = Crear(origen: OrigenMaster.Aw, referenciaExterna: "56380");

        c.ActualizarDatos(rfc: "GCO123456AB9", regimenFiscal: "601", codigoPostalFiscal: "97300");
        Assert.True(c.DatosFiscalesCompletos);

        c.ActualizarDatos(limpiarRfc: true);
        Assert.Null(c.Rfc);
        Assert.False(c.DatosFiscalesCompletos);
        // Inmutables: la referencia externa no se toca desde ActualizarDatos.
        Assert.Equal("56380", c.ReferenciaExterna);
        Assert.Equal(OrigenMaster.Aw, c.Origen);
    }

    [Fact]
    public void CambiarEstatus_SofDelete_Idempotente()
    {
        var c = Crear();
        c.CambiarEstatus(EstatusCatalogo.Inactivo);
        c.CambiarEstatus(EstatusCatalogo.Inactivo);
        Assert.Equal(EstatusCatalogo.Inactivo, c.Estatus);
    }

    [Theory]
    [InlineData("US")]       // 2 letras
    [InlineData("USAA")]     // 4 letras
    [InlineData("12A")]      // con dígito
    public void Should_Throw_When_PaisResidenciaInvalido(string pais)
    {
        var c = Crear();
        var ex = Assert.Throws<BusinessRuleException>(
            () => c.AsignarDatosReceptorExtranjero(paisResidencia: pais));
        Assert.Equal("CLIENTE_PAIS_RESIDENCIA_INVALIDO", ex.Code);
    }

    [Fact]
    public void AsignarDatosReceptorExtranjero_Fija_YLimpiaConFlags()
    {
        var c = Crear();
        c.AsignarDatosReceptorExtranjero(
            numRegIdTrib: "US123456789",
            paisResidencia: "USA",
            domicilioExtranjeroCalle: "123 Main St",
            domicilioExtranjeroEstado: "Texas",
            domicilioExtranjeroCodigoPostal: "75001");
        Assert.Equal("US123456789", c.NumRegIdTrib);
        Assert.Equal("USA", c.PaisResidencia);
        Assert.Equal("Texas", c.DomicilioExtranjeroEstado);

        c.AsignarDatosReceptorExtranjero(limpiarNumRegIdTrib: true, limpiarPaisResidencia: true);
        Assert.Null(c.NumRegIdTrib);
        Assert.Null(c.PaisResidencia);
        Assert.Equal("Texas", c.DomicilioExtranjeroEstado); // no tocada
    }
}
