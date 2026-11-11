using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Application.Ports;
using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ComercioExterior.UnitTests;

/// <summary>M-187: el gasto acepta el tipo de cambio capturado a mano aunque el catálogo no tenga fila Banxico.</summary>
public sealed class TipoCambioGastoResolverTests
{
    private static readonly DateOnly Fecha = new(2026, 10, 21);
    private sealed class CatalogoFalso(TipoCambioCatalogo? fila) : ITipoCambioGastoReadPort
    {
        public int Consultas { get; private set; }
        public Task<TipoCambioCatalogo?> ObtenerSugeridoAsync(DateOnly fecha, CancellationToken ct) { Consultas++; return Task.FromResult(fila); }
    }
    private static Task<TipoCambioGastoResuelto> Resolver(TipoCambioCatalogo? fila, string? fuente, decimal? usado = null, decimal? cfdi = null) =>
        TipoCambioGastoResolver.ResolverAsync(new CatalogoFalso(fila), "USD", Fecha, fuente, usado, cfdi, CancellationToken.None);

    [Fact]
    public async Task Rama1_fuente_capturada_no_falla_sin_fila_en_el_catalogo()
    {
        var negociado = await Resolver(null, "Negociado", usado: 18.30m);
        Assert.Equal(new TipoCambioGastoResuelto(18.30m, "Negociado", null), negociado);
        var cfdi = await Resolver(null, "CFDI", cfdi: 18.35m);
        Assert.Equal(new TipoCambioGastoResuelto(18.35m, "CFDI", null), cfdi);
    }

    [Fact]
    public async Task Rama1_fuente_capturada_con_fila_manual_la_usa_solo_para_comparar()
    {
        var r = await Resolver(new(18.50m, "Manual"), "Pedimento", usado: 18.30m);
        Assert.Equal(new TipoCambioGastoResuelto(18.30m, "Pedimento", 18.50m), r);
    }

    [Theory]
    [InlineData("Manual", "Catálogo Manual")]
    [InlineData("DOF", "Catálogo DOF")]
    [InlineData("Banxico", "Banxico")]
    public async Task Rama2_banxico_acepta_la_fila_de_la_fecha_y_muestra_su_origen(string origen, string fuente)
    {
        var r = await Resolver(new(18.4253m, origen), "Banxico");
        Assert.Equal(new TipoCambioGastoResuelto(18.4253m, fuente, 18.4253m), r);
        Assert.Equal(r, await Resolver(new(18.4253m, origen), null));
    }

    [Fact]
    public async Task Rama3_banxico_sin_fila_conserva_el_error_actual()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Resolver(null, "Banxico"));
        Assert.Equal("CE_TC_BANXICO_FALTANTE", ex.Code);
        Assert.Equal("Registre el tipo de cambio Banxico de la fecha de la factura en el catálogo de monedas.", ex.Message);
        Assert.Equal("CE_TC_BANXICO_FALTANTE", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => TipoCambioGastoResolver.ObligatorioAsync(new CatalogoFalso(null), Fecha, CancellationToken.None))).Code);
    }

    [Fact]
    public async Task Mxn_no_consulta_el_catalogo()
    {
        var catalogo = new CatalogoFalso(null);
        Assert.Equal(new TipoCambioGastoResuelto(1, "MXN", 1),
            await TipoCambioGastoResolver.ResolverAsync(catalogo, "MXN", Fecha, null, null, null, CancellationToken.None));
        Assert.Equal(0, catalogo.Consultas);
    }

    [Fact]
    public async Task El_cliente_no_puede_enviar_una_fuente_de_catalogo_para_saltarse_la_nota() =>
        Assert.Equal("CE_TIPO_CAMBIO_FUENTE_INVALIDA", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => Resolver(new(18.4253m, "Manual"), "Catálogo Manual", usado: 10m))).Code);

    [Fact]
    public async Task Fuente_capturada_sin_valor_se_rechaza() =>
        Assert.Equal("CE_TIPO_CAMBIO_REQUERIDO", (await Assert.ThrowsAsync<BusinessRuleException>(() => Resolver(null, "Negociado"))).Code);

    [Fact]
    public async Task El_gasto_se_guarda_con_fuente_manual_sin_sugerido_y_con_origen_de_catalogo()
    {
        var e = new Expediente(Guid.NewGuid(), Guid.NewGuid(), "CE-TC", TipoExpediente.Importacion, Guid.NewGuid(), "TC", Guid.NewGuid(), Guid.NewGuid(), "MXN", Guid.NewGuid(), []);
        var c = new ConceptoGasto(e.EmpresaId, "FLETE", "Flete");
        DatosGasto d = new(c.Id, Guid.NewGuid(), "F-1", "USD", 1000, Fecha);
        var manual = await Resolver(null, "Negociado", usado: 18.30m);
        e.GuardarGasto(Guid.NewGuid(), d, c, manual.TipoCambio, manual.Fuente, null, manual.Sugerido, "Pactado con el agente", Guid.NewGuid());
        var g = e.Gastos.Single();
        Assert.Equal(18300.00m, g.ImporteMxn); Assert.Null(g.TipoCambioSugerido); Assert.Null(g.DiferenciaTipoCambioPct);

        var catalogo = await Resolver(new(18.50m, "DOF"), "Banxico");
        e.GuardarGasto(Guid.NewGuid(), d with { Factura = "F-2" }, c, 17m, catalogo.Fuente, null, catalogo.Sugerido);
        var g2 = e.Gastos.Single(x => x.Factura == "F-2");
        Assert.Equal("Catálogo DOF", g2.FuenteTipoCambio); Assert.Equal(18500.00m, g2.ImporteMxn);
        Assert.Null(g2.NotaTipoCambio); Assert.Null(g2.TipoCambioModificadoPor); Assert.Equal(0, g2.DiferenciaTipoCambioPct);
    }
}
