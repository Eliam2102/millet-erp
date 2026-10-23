using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Recepciones;

// Todos los documentos de estas pruebas son ficticios (DEMO).
public class ImportacionRecepcionTests
{
    private static readonly Guid Linea = Guid.NewGuid();
    private static readonly Guid Articulo = Guid.NewGuid();
    private static OcLectura Oc(string? pedimento = null, string? bl = null, string? contenedor = null) =>
        new(Guid.NewGuid(), "OC-DEMO", Guid.NewGuid(), Guid.NewGuid(), "Autorizada",
            [new(Linea, Articulo, "PZA", 2, 0, 100, FraccionArancelaria: "70071999")],
            EsImportacion: true, NumeroPedimento: pedimento, NumeroBl: bl, NumeroContenedor: contenedor);
    private static RegistrarRecepcionLineaInput[] Partidas(string? fraccion = null) =>
        [new(Articulo, Linea, 1, null, null, FraccionArancelaria: fraccion)];

    [Fact]
    public void CA39_Exige_los_tres_documentos_al_recibir()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => ImportacionRecepcionGuard.Crear(Oc(), null, Partidas()));
        Assert.Contains("pedimento", ex.Message); Assert.Contains("BL", ex.Message); Assert.Contains("contenedor", ex.Message);
    }

    [Theory]
    [InlineData(null, "BL-DEMO", "CONT-DEMO", "pedimento")]
    [InlineData("26 16 1234 6000001", null, "CONT-DEMO", "BL")]
    [InlineData("261612346000001", "BL-DEMO", null, "contenedor")]
    public void CA39_Identifica_cada_faltante(string? pedimento, string? bl, string? contenedor, string falta)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => ImportacionRecepcionGuard.Crear(Oc(pedimento, bl, contenedor), null, Partidas()));
        Assert.Contains(falta, ex.Message);
    }

    [Fact]
    public void Linea_que_no_es_de_la_OC_da_error_de_negocio()
    {
        RegistrarRecepcionLineaInput[] ajena = [new(Articulo, Guid.NewGuid(), 1, null, null)];
        var ex = Assert.Throws<BusinessRuleException>(() => ImportacionRecepcionGuard.Crear(
            Oc(), new("261612346000001", "BL-DEMO", "CONT-DEMO", "16", "A1"), ajena));
        Assert.Equal("RECEPCION_LINEA_OC_NO_ENCONTRADA", ex.Code);
    }

    [Fact]
    public void CA39_Normaliza_y_conserva_aduana_clave_y_fraccion_editada()
    {
        var datos = ImportacionRecepcionGuard.Crear(Oc(), new("26 16 1234 6000001", "BL-DEMO", "CONT-DEMO", "16", "A1"), Partidas("7007199900"))!;
        Assert.Equal("261612346000001", datos.NumeroPedimento);
        Assert.Equal("16", datos.Aduana); Assert.Equal("A1", datos.ClavePedimento);
        Assert.Equal("7007199900", Assert.Single(datos.Partidas).FraccionArancelaria);
    }

    [Fact]
    public void CA39_Usa_documentos_y_fraccion_de_la_OC()
    {
        var datos = ImportacionRecepcionGuard.Crear(Oc("261612346000001", "BL-DEMO", "CONT-DEMO"), new(null, null, null, "16", "A1"), Partidas())!;
        Assert.Equal("BL-DEMO", datos.NumeroBl);
        Assert.Equal("70071999", Assert.Single(datos.Partidas).FraccionArancelaria);
    }

    [Theory]
    [InlineData("123", "16", "A1", "70071999")]
    [InlineData("26161234600000X", "16", "A1", "70071999")]
    [InlineData("261612346000001", "XX", "A1", "70071999")]
    [InlineData("261612346000001", "16", "ZZ", "70071999")]
    [InlineData("261612346000001", "16", "A1", "123")]
    public void CA39_Rechaza_formato_o_catalogo_invalido(string numero, string aduana, string clave, string fraccion) =>
        Assert.Throws<BusinessRuleException>(() => ImportacionRecepcionGuard.Crear(Oc(), new(numero, "BL-DEMO", "CONT-DEMO", aduana, clave), Partidas(fraccion)));

    [Fact]
    public void Compra_nacional_no_exige_datos_aduanales() =>
        Assert.Null(ImportacionRecepcionGuard.Crear(Oc() with { EsImportacion = false }, null, Partidas()));
}
