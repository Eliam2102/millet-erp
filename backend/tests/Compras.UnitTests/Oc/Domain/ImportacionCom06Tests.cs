using Millet.Compras.Domain.Oc;
namespace Millet.Compras.UnitTests.Oc.Domain;
public class ImportacionCom06Tests
{
    [Fact]
    public void C34a_Autorizar_no_exige_pedimento_BL_ni_contenedor()
    {
        var info = new InformacionImportacion(incotermId: Guid.NewGuid(), paisOrigen: "CN", codigoRuta: "DEMO", semanaEmbarque: "2026-W43");
        info.ValidarParaAutorizar();
        Assert.Null(info.NumeroPedimento); Assert.Null(info.NumeroBl);
    }
    [Fact]
    public void Pedimento_se_normaliza_en_la_OC() =>
        Assert.Equal("261612346000001", new InformacionImportacion(numeroPedimento: "26 16 1234 6000001").NumeroPedimento);
    [Fact]
    public void Pedimento_guardado_con_formato_anterior_no_impide_leer_la_OC() =>
        Assert.Equal("PED-2026-77", new InformacionImportacion(numeroPedimento: "PED-2026-77").NumeroPedimento);
}
