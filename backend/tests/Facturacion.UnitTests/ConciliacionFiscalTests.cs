using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Conciliacion;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests;

/// <summary>M-314 · folios del periodo y resolución de la incidencia fiscal (lógica de dominio). Datos FICTICIOS.</summary>
public class ConciliacionFiscalTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("FV-000123", "FV-")]
    [InlineData("A12", "A")]
    [InlineData("000045", "")]
    [InlineData("SINNUMERO", "SINNUMERO")]
    public void La_serie_es_el_prefijo_del_folio(string folio, string serie) => AnalisisFolios.SerieDe(folio).Should().Be(serie);

    [Fact]
    public void Una_serie_sin_huecos_ni_pendientes_lista_usados_cancelados_y_descartados_y_puede_cerrar()
    {
        var a = AnalisisFolios.Analizar([(1, EstadoTimbrado.Timbrado), (2, EstadoTimbrado.Cancelado), (3, EstadoTimbrado.Descartada), (4, EstadoTimbrado.Timbrado)]);

        a.Usados.Should().Equal(1, 4);
        a.Cancelados.Should().Equal(2);
        a.Descartados.Should().Equal(3);
        a.Pendientes.Should().BeEmpty();
        a.Huecos.Should().BeEmpty();
        (a.FolioInicial, a.FolioFinal).Should().Be((1L, 4L));
        a.PuedeCerrar.Should().BeTrue();
    }

    [Fact]
    public void Un_hueco_o_un_folio_pendiente_impiden_el_cierre()
    {
        var hueco = AnalisisFolios.Analizar([(1, EstadoTimbrado.Timbrado), (3, EstadoTimbrado.Timbrado)]);
        hueco.Huecos.Should().Equal(2);
        hueco.PuedeCerrar.Should().BeFalse();

        var pendiente = AnalisisFolios.Analizar([(1, EstadoTimbrado.Timbrado), (2, EstadoTimbrado.Borrador), (3, EstadoTimbrado.TimbradoFallido)]);
        pendiente.Pendientes.Should().Equal(2, 3);
        pendiente.PuedeCerrar.Should().BeFalse();
        pendiente.Motivo("FV-").Should().Contain("pendientes").And.Contain("FV-");
    }

    [Fact]
    public void Sin_folios_no_hay_nada_que_cerrar() => AnalisisFolios.Analizar([]).PuedeCerrar.Should().BeFalse();

    [Fact]
    public void El_cierre_nace_vigente_con_el_reporte_y_no_se_crea_si_la_serie_no_cumple()
    {
        var ok = AnalisisFolios.Analizar([(1, EstadoTimbrado.Timbrado), (2, EstadoTimbrado.Cancelado)]);
        var cierre = new CierreFolios(Guid.NewGuid(), Guid.NewGuid(), 2026, 10, TipoComprobante.Ingreso, "FV-", ok, "{}", "usuario", Ahora);
        cierre.Vigente.Should().BeTrue();
        (cierre.Usados, cierre.Cancelados, cierre.Descartados).Should().Be((1, 1, 0));

        var mal = AnalisisFolios.Analizar([(1, EstadoTimbrado.Timbrado), (3, EstadoTimbrado.Timbrado)]);
        var act = () => new CierreFolios(Guid.NewGuid(), Guid.NewGuid(), 2026, 10, TipoComprobante.Ingreso, "FV-", mal, "{}", "usuario", Ahora);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("FAC_CIERRE_FOLIOS_NO_CUMPLE");
    }

    [Fact]
    public void Reabrir_exige_motivo_deja_bitacora_y_no_se_repite()
    {
        var ok = AnalisisFolios.Analizar([(1, EstadoTimbrado.Timbrado)]);
        var cierre = new CierreFolios(Guid.NewGuid(), Guid.NewGuid(), 2026, 10, TipoComprobante.Ingreso, "FV-", ok, "{}", "usuario", Ahora);

        var sinMotivo = () => cierre.Reabrir("no", "supervisor", Ahora);
        sinMotivo.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("FAC_CIERRE_FOLIOS_MOTIVO_REQUERIDO");

        cierre.Reabrir("Corrección de un folio descartado", "supervisor", Ahora);
        (cierre.Vigente, cierre.ReabiertoPor, cierre.ReabiertoEn).Should().Be((false, "supervisor", Ahora));

        var otra = () => cierre.Reabrir("Otra vez por favor", "supervisor", Ahora);
        otra.Should().Throw<ConflictException>().Which.Code.Should().Be("FAC_CIERRE_FOLIOS_YA_REABIERTO");
    }

    [Fact]
    public void La_resolucion_de_la_incidencia_exige_soporte()
    {
        var sin = () => new ResolucionIncidenciaFiscal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "UUID", Guid.NewGuid(),
            EstadoTimbrado.Timbrado, "Cancelado", "Vigente", " ", null, "usuario", Ahora);
        sin.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("FAC_INCIDENCIA_SOPORTE_REQUERIDO");

        var r = new ResolucionIncidenciaFiscal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "UUID", Guid.NewGuid(),
            EstadoTimbrado.Timbrado, "Cancelado", "Vigente", "  Acuse del portal del SAT  ", "  ", "usuario", Ahora);
        (r.Soporte, r.Nota, r.EstatusSatAntes, r.EstatusSatDespues).Should().Be(("Acuse del portal del SAT", null, "Cancelado", "Vigente"));
    }
}
