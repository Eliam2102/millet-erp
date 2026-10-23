using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.NotasCredito;

public sealed class ReembolsoNotaCreditoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DatosFiscalesEmisor Emisor = new("BBB010101BBB", "Millet", "601", "76120");
    private static DatosFiscalesReceptor Receptor() => new("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);

    private static NotaCredito Bonificacion(bool timbrada = true)
    {
        var nc = NotaCredito.CrearBonificacion(
            Guid.NewGuid(), "NC-1", 1, Guid.NewGuid(), null, null, Receptor(), Emisor, "03", "MXN", null, 2026, 10,
            canalVentaId: 1, facturaRelacionadaId: Guid.NewGuid(), montoTotal: 116m, tasaIva: 0.16m, descripcion: null);
        if (timbrada)
        {
            nc.MarcarTimbradoEnProceso();
            nc.MarcarTimbrado("22222222-2222-2222-2222-222222222222", null, null, null, Ahora, null, null);
        }
        return nc;
    }

    [Fact]
    public void Bonificacion_timbrada_fija_las_propiedades()
    {
        var nc = Bonificacion();
        nc.SolicitarReembolso(100m, Ahora);
        nc.ReembolsoSolicitadoEn.Should().Be(Ahora);
        nc.ImporteReembolsoSolicitado.Should().Be(100m);
    }

    [Fact]
    public void Segunda_solicitud_lanza_YA_SOLICITADO()
    {
        var nc = Bonificacion();
        nc.SolicitarReembolso(100m, Ahora);
        var act = () => nc.SolicitarReembolso(10m, Ahora);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NC_REEMBOLSO_YA_SOLICITADO");
    }

    [Fact]
    public void Importe_mayor_al_total_lanza_IMPORTE_INVALIDO()
    {
        var act = () => Bonificacion().SolicitarReembolso(116.01m, Ahora);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NC_REEMBOLSO_IMPORTE_INVALIDO");
    }

    [Fact]
    public void NC_en_borrador_lanza_NO_TIMBRADA()
    {
        var act = () => Bonificacion(timbrada: false).SolicitarReembolso(10m, Ahora);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NC_REEMBOLSO_NO_TIMBRADA");
    }

    [Fact]
    public void NC_de_amortizacion_lanza_MOTIVO_NO_PERMITIDO()
    {
        var nc = NotaCredito.CrearAmortizacion(
            Guid.NewGuid(), "NC-2", 2, Guid.NewGuid(), null, null, Receptor(), Emisor, "03", "MXN", null, 2026, 10,
            1, Guid.NewGuid(), Guid.NewGuid(), 116m, 0.16m);
        nc.MarcarTimbradoEnProceso();
        nc.MarcarTimbrado("33333333-3333-3333-3333-333333333333", null, null, null, Ahora, null, null);
        var act = () => nc.SolicitarReembolso(10m, Ahora);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NC_REEMBOLSO_MOTIVO_NO_PERMITIDO");
    }
}
