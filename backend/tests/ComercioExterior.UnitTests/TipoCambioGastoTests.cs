using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ComercioExterior.UnitTests;
public sealed class TipoCambioGastoTests
{
    private static readonly DateOnly Fecha = new(2026, 10, 21);
    private static (Expediente e, ConceptoGasto c) Nuevo()
    {
        var e = new Expediente(Guid.NewGuid(), Guid.NewGuid(), "CE-TC", TipoExpediente.Importacion, Guid.NewGuid(), "TC", Guid.NewGuid(), Guid.NewGuid(), "MXN", Guid.NewGuid(), []);
        return (e, new ConceptoGasto(e.EmpresaId, "FLETE", "Flete"));
    }
    private static GastoExpediente Guardar(decimal tc, string fuente, string? nota = null, decimal sugerido = 18.4253m)
    {
        var (e, c) = Nuevo(); var usuario = Guid.NewGuid();
        e.GuardarGasto(Guid.NewGuid(), new(c.Id, Guid.NewGuid(), "F-1", "USD", 1000, Fecha), c, tc, fuente, null, sugerido, nota, usuario);
        var g = e.Gastos.Single(); if (fuente is not ("Banxico" or "MXN")) Assert.Equal(usuario, g.TipoCambioModificadoPor);
        return g;
    }
    [Fact]
    public void CA1_sin_cambio_usa_banxico()
    {
        var g = Guardar(18.4253m, "Banxico");
        Assert.Equal(18425.30m, g.ImporteMxn); Assert.Equal("Banxico", g.FuenteTipoCambio);
        Assert.Equal(0, g.DiferenciaTipoCambioPct); Assert.Null(g.TipoCambioModificadoPor);
    }
    [Fact]
    public void CA2_negociado_guarda_sugerido_diferencia_y_usuario()
    {
        var g = Guardar(18.30m, "Negociado", "Pactado con el agente");
        Assert.Equal(18300.00m, g.ImporteMxn); Assert.Equal(18.4253m, g.TipoCambioSugerido);
        Assert.Equal(-0.68m, g.DiferenciaTipoCambioPct); Assert.Equal("Pactado con el agente", g.NotaTipoCambio);
    }
    [Fact]
    public void CA3_cfdi_con_tipo_de_cambio_de_la_factura() => Assert.Equal(18350.00m, Guardar(18.35m, "CFDI", "TC del CFDI de la factura").ImporteMxn);
    [Theory]
    [InlineData(null)]
    [InlineData("corta")]
    [InlineData("          ")]
    public void CA4_cambio_sin_nota_valida_se_rechaza(string? nota) =>
        Assert.Equal("CE_TIPO_CAMBIO_NOTA_REQUERIDA", Assert.Throws<BusinessRuleException>(() => Guardar(18.30m, "Negociado", nota)).Code);
    [Fact]
    public void Fuente_desconocida_se_rechaza() =>
        Assert.Equal("CE_TIPO_CAMBIO_NOTA_REQUERIDA", Assert.Throws<BusinessRuleException>(() => Guardar(18.30m, "Otra", "Nota suficiente")).Code);
    [Fact]
    public void Banxico_ignora_el_tc_enviado_y_usa_el_sugerido()
    {
        var g = Guardar(17.00m, "Banxico");
        Assert.Equal(18.4253m, g.TipoCambio); Assert.Equal(18425.30m, g.ImporteMxn);
        Assert.Equal(0, g.DiferenciaTipoCambioPct); Assert.Null(g.NotaTipoCambio); Assert.Null(g.TipoCambioModificadoPor);
    }
    [Fact]
    public void Fuente_mxn_en_gasto_usd_se_rechaza() =>
        Assert.Equal("CE_TIPO_CAMBIO_FUENTE_INVALIDA", Assert.Throws<BusinessRuleException>(() => Guardar(5m, "MXN")).Code);
    [Fact]
    public void Editar_a_banxico_con_tc_falso_no_conserva_el_manual()
    {
        var (e, c) = Nuevo(); var id = Guid.NewGuid();
        DatosGasto d = new(c.Id, Guid.NewGuid(), "F-1", "USD", 1000, Fecha);
        e.GuardarGasto(id, d, c, 18.30m, "Negociado", null, 18.4253m, "Pactado con el agente", Guid.NewGuid());
        e.GuardarGasto(id, d, c, 17.00m, "Banxico", null, 18.4253m);
        var g = e.Gastos.Single();
        Assert.Equal(18.4253m, g.TipoCambio); Assert.Null(g.NotaTipoCambio); Assert.Null(g.TipoCambioModificadoPor);
    }
}
