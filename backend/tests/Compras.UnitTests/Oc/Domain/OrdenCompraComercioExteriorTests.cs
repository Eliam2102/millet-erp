using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests de los campos de comercio exterior de la OC (supuestos Vilo,
/// PLATFORM-TODO(F1-COM-06)): TipoOperacion por defecto, editabilidad
/// pre y post-autorización, y fracción arancelaria de la línea.
/// </summary>
public class OrdenCompraComercioExteriorTests
{
    private static OrdenCompra NewOc(bool esImportacion) => new(
        id: Guid.CreateVersion7(),
        empresaId: Guid.CreateVersion7(),
        folio: Folio.Parse("OC-MID2026-000001"),
        folioAnio: 2026,
        proveedorId: Guid.CreateVersion7(),
        sucursalDestinoId: Guid.CreateVersion7(),
        condicionesPagoId: Guid.CreateVersion7(),
        usoPrincipalId: Guid.CreateVersion7(),
        compradorTitularId: Guid.CreateVersion7(),
        encargadoComprasId: Guid.CreateVersion7(),
        fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
        esImportacion: esImportacion,
        sinRequisicionPrevia: true,
        motivoSinRequisicion: "Test comercio exterior");

    private static LineaOrdenCompra AgregarLinea(OrdenCompra oc, string? fraccion = null) =>
        oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 10m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7(),
            centroCostoId: Guid.CreateVersion7(),
            fraccionArancelaria: fraccion);

    private static InformacionImportacion InfoBasica(TipoOperacionComercioExterior? tipo = null) => new(
        incotermId: Guid.CreateVersion7(),
        paisOrigen: "US",
        numeroContenedor: "CONT-001",
        codigoRuta: "RUTA-01",
        semanaEmbarque: "S10",
        tipoOperacion: tipo);

    // --- VO InformacionImportacion ---

    [Fact]
    public void InformacionImportacion_NumeroBlDemasiadoLargo_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            new InformacionImportacion(numeroBl: new string('B', 51)));
        Assert.Equal("IMPORT_BL_DEMASIADO_LARGO", ex.Code);
    }

    [Fact]
    public void InformacionImportacion_SoloCertificadoOrigen_NoEsVacio()
    {
        Assert.True(new InformacionImportacion().EsVacio);
        Assert.False(new InformacionImportacion(certificadoOrigen: true).EsVacio);
    }

    // --- TipoOperacion default ---

    [Fact]
    public void ActualizarInformacionImportacion_OcImportacion_DefaultaTipoAImportacion()
    {
        var oc = NewOc(esImportacion: true);
        oc.ActualizarInformacionImportacion(InfoBasica());

        Assert.Equal(TipoOperacionComercioExterior.Importacion, oc.InfoImportTipoOperacion);
        Assert.Equal(TipoOperacionComercioExterior.Importacion, oc.InformacionImportacion!.Value.TipoOperacion);
    }

    [Fact]
    public void ActualizarInformacionImportacion_Exportacion_ConservaTipo()
    {
        var oc = NewOc(esImportacion: true);
        oc.ActualizarInformacionImportacion(InfoBasica(TipoOperacionComercioExterior.Exportacion));

        Assert.Equal(TipoOperacionComercioExterior.Exportacion, oc.InfoImportTipoOperacion);
    }

    [Fact]
    public void ActualizarInformacionImportacion_OcNoImportacion_NoAsignaTipo()
    {
        var oc = NewOc(esImportacion: false);
        oc.ActualizarInformacionImportacion(InfoBasica());

        Assert.Null(oc.InfoImportTipoOperacion);
    }

    [Fact]
    public void ActualizarInformacionImportacion_TodoVacio_LimpiaInformacion()
    {
        var oc = NewOc(esImportacion: true);
        oc.ActualizarInformacionImportacion(new InformacionImportacion(certificadoOrigen: true));
        Assert.True(oc.InfoImportCertificadoOrigen);

        oc.ActualizarInformacionImportacion(null);

        Assert.Null(oc.InformacionImportacion);
        Assert.False(oc.InfoImportCertificadoOrigen);
    }

    // --- Editabilidad por estado ---

    [Fact]
    public void ActualizarInformacionImportacion_EnAutorizacion_Lanza()
    {
        var oc = NewOc(esImportacion: true);
        AgregarLinea(oc);
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.ActualizarInformacionImportacion(InfoBasica()));
        Assert.Equal("OC_IMPORTACION_SOLO_EDITABLE", ex.Code);
    }

    [Fact]
    public void ActualizarNumeroPedimento_PostEnvio_GuardaPedimentoBlEtaYAgente()
    {
        var oc = NewOc(esImportacion: true);
        AgregarLinea(oc);
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        var eta = new DateOnly(2026, 11, 3);

        oc.ActualizarNumeroPedimento("PED-12345", "BL-998877", eta, "Agencia Aduanal X");

        Assert.Equal("PED-12345", oc.InfoImportNumeroPedimento);
        Assert.Equal("BL-998877", oc.InfoImportNumeroBl);
        Assert.Equal(eta, oc.InfoImportFechaEstimadaArribo);
        Assert.Equal("Agencia Aduanal X", oc.InfoImportAgenteAduanalNaviera);
    }

    [Fact]
    public void ActualizarNumeroPedimento_AgenteDemasiadoLargo_Lanza()
    {
        var oc = NewOc(esImportacion: true);
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.ActualizarNumeroPedimento(null, agenteAduanalNaviera: new string('A', 121)));
        Assert.Equal("IMPORT_AGENTE_DEMASIADO_LARGO", ex.Code);
    }

    // --- Fracción arancelaria de la línea ---

    [Theory]
    [InlineData("84719000")]
    [InlineData("8471900010")]
    public void AgregarLineaManual_FraccionValida_LaConserva(string fraccion)
    {
        var oc = NewOc(esImportacion: true);
        var linea = AgregarLinea(oc, fraccion);

        Assert.Equal(fraccion, linea.FraccionArancelaria);
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("8471-900")]
    [InlineData("84719ABC")]
    public void ActualizarLinea_FraccionInvalida_Lanza(string fraccion)
    {
        var oc = NewOc(esImportacion: true);
        var linea = AgregarLinea(oc);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.ActualizarLinea(lineaId: linea.Id, fraccionArancelaria: fraccion));
        Assert.Equal("LINEA_OC_FRACCION_INVALIDA", ex.Code);
    }

    [Fact]
    public void ActualizarLinea_FraccionEnOcNoImportacion_Lanza()
    {
        var oc = NewOc(esImportacion: false);
        var linea = AgregarLinea(oc);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.ActualizarLinea(lineaId: linea.Id, fraccionArancelaria: "84719000"));
        Assert.Equal("LINEA_OC_FRACCION_SOLO_COMERCIO_EXTERIOR", ex.Code);
    }

    [Fact]
    public void ActualizarLinea_LimpiarFraccion_DejaNull()
    {
        var oc = NewOc(esImportacion: true);
        var linea = AgregarLinea(oc, "84719000");

        oc.ActualizarLinea(lineaId: linea.Id, limpiarFraccionArancelaria: true);

        Assert.Null(linea.FraccionArancelaria);
    }
}
