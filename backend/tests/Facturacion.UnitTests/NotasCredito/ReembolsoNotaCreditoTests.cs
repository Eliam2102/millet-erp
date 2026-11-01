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

    // ---- F6.3: reembolso programado al emitir y estado reportado por Tesorería ----

    private const string ClabeValida = "032180000118359719";

    [Fact]
    public void F63_programar_en_borrador_queda_pendiente_hasta_timbrar()
    {
        var nc = Bonificacion(timbrada: false);
        nc.ProgramarReembolso(" BBVA ", ClabeValida, " Cliente SA ");

        nc.ReembolsoAlTimbrar.Should().BeTrue();
        nc.ReembolsoBanco.Should().Be("BBVA");
        nc.ReembolsoTitular.Should().Be("Cliente SA");
        nc.DebeSolicitarReembolsoProgramado.Should().BeFalse("aún no está timbrada");

        nc.MarcarTimbradoEnProceso();
        nc.MarcarTimbrado("44444444-4444-4444-4444-444444444444", null, null, null, Ahora, null, null);
        nc.DebeSolicitarReembolsoProgramado.Should().BeTrue();
        nc.SolicitarReembolso(nc.Total, Ahora);
        nc.DebeSolicitarReembolsoProgramado.Should().BeFalse("ya se solicitó");
        nc.EstadoReembolso.Should().Be(EstadoReembolsoNotaCredito.Solicitado);
    }

    [Fact]
    public void F63b_sin_programar_no_hay_que_solicitar_reembolso()
        => Bonificacion().DebeSolicitarReembolsoProgramado.Should().BeFalse();

    [Fact]
    public void F63_clabe_con_digito_verificador_invalido_se_rechaza()
    {
        var act = () => Bonificacion(timbrada: false).ProgramarReembolso("BBVA", "032180000118359710", "Cliente SA");
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void F63_programar_en_una_nc_ya_timbrada_lanza_YA_TIMBRADA()
    {
        var act = () => Bonificacion().ProgramarReembolso("BBVA", ClabeValida, "Cliente SA");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NC_REEMBOLSO_YA_TIMBRADA");
    }

    [Fact]
    public void F63_sin_banco_o_titular_lanza_DESTINO_INCOMPLETO()
    {
        var act = () => Bonificacion(timbrada: false).ProgramarReembolso(" ", ClabeValida, "Cliente SA");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NC_REEMBOLSO_DESTINO_INCOMPLETO");
    }

    [Fact]
    public void F63a_pagado_guarda_fecha_y_referencia_y_es_final()
    {
        var nc = Bonificacion();
        nc.SolicitarReembolso(116m, Ahora);
        nc.RegistrarEstadoReembolso(EstadoReembolsoNotaCredito.Autorizado, Ahora.AddDays(1), null, null, null);
        nc.RegistrarEstadoReembolso(EstadoReembolsoNotaCredito.Pagado, Ahora.AddDays(2), new DateOnly(2026, 10, 19), " SPEI 123 ", null);

        nc.EstadoReembolso.Should().Be(EstadoReembolsoNotaCredito.Pagado);
        nc.ReembolsoPagadoEl.Should().Be(new DateOnly(2026, 10, 19));
        nc.ReembolsoReferencia.Should().Be("SPEI 123");
        nc.ReembolsoActualizadoEn.Should().Be(Ahora.AddDays(2));

        // Un «Autorizado» que llega tarde (desorden o reintento) no retrocede el estado.
        nc.RegistrarEstadoReembolso(EstadoReembolsoNotaCredito.Autorizado, Ahora.AddDays(3), null, null, null);
        nc.EstadoReembolso.Should().Be(EstadoReembolsoNotaCredito.Pagado);
    }

    [Fact]
    public void F63_rechazado_guarda_el_motivo()
    {
        var nc = Bonificacion();
        nc.SolicitarReembolso(116m, Ahora);
        nc.RegistrarEstadoReembolso(EstadoReembolsoNotaCredito.Rechazado, Ahora, null, null, " Cuenta no coincide ");
        nc.EstadoReembolso.Should().Be(EstadoReembolsoNotaCredito.Rechazado);
        nc.ReembolsoMotivoResolucion.Should().Be("Cuenta no coincide");
    }

    [Fact]
    public void F63_estado_sin_solicitud_lanza_NO_SOLICITADO()
    {
        var act = () => Bonificacion().RegistrarEstadoReembolso(EstadoReembolsoNotaCredito.Pagado, Ahora, null, null, null);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NC_REEMBOLSO_NO_SOLICITADO");
    }
}
