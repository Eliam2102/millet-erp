using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.UnitTests;

/// <summary>M-321: incidencia posterior; se resuelve con póliza de ajuste o reapertura acotada. Datos genéricos.</summary>
public class IncidenciaPosteriorTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Poliza = Guid.NewGuid();

    private static IncidenciaPosterior Nueva(string? hallazgo = "Importe duplicado en una póliza autorizada", string? impacto = "Gasto sobrestimado", decimal? importe = 1_250m) =>
        new(Guid.NewGuid(), Poliza, 2026, 10, hallazgo, impacto, importe, Guid.NewGuid(), "Contador de prueba", Ahora);

    [Fact]
    public void Nace_abierta_con_quien_la_registro_y_su_impacto()
    {
        var i = Nueva();
        i.Abierta.Should().BeTrue();
        (i.PeriodoAnio, i.PeriodoNumero, i.ImporteImpacto, i.RegistradaPor).Should().Be((2026, 10, 1_250m, "Contador de prueba"));
    }

    [Theory]
    [InlineData("abc", "Gasto sobrestimado", "CONTAB_INCIDENCIA_HALLAZGO_INVALIDO")]
    [InlineData(null, "Gasto sobrestimado", "CONTAB_INCIDENCIA_HALLAZGO_INVALIDO")]
    [InlineData("Importe duplicado en una póliza", "no", "CONTAB_INCIDENCIA_IMPACTO_INVALIDO")]
    public void Exige_hallazgo_e_impacto(string? hallazgo, string impacto, string codigo)
    {
        var crear = () => Nueva(hallazgo, impacto);
        crear.Should().Throw<BusinessRuleException>().Where(e => e.Code == codigo);
    }

    [Fact]
    public void El_importe_de_impacto_no_es_negativo()
    {
        var crear = () => Nueva(importe: -1m);
        crear.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CONTAB_INCIDENCIA_IMPORTE_INVALIDO");
    }

    [Fact]
    public void Al_ligar_su_poliza_de_ajuste_queda_resuelta()
    {
        var i = Nueva();
        var ajuste = Guid.NewGuid();
        i.ResolverConPolizaAjuste(ajuste, "Se registró el ajuste", Guid.NewGuid(), "Otra persona", Ahora.AddDays(1));
        (i.Estado, i.Resolucion, i.PolizaAjusteId, i.ResueltaPor).Should().Be((EstadoIncidenciaPosterior.Resuelta, ResolucionIncidencia.PolizaAjuste, ajuste, "Otra persona"));
        i.Abierta.Should().BeFalse();
    }

    [Fact]
    public void La_poliza_de_ajuste_no_puede_ser_la_misma_de_la_incidencia()
    {
        var i = Nueva();
        var resolver = () => i.ResolverConPolizaAjuste(Poliza, null, null, "x", Ahora);
        resolver.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CONTAB_INCIDENCIA_AJUSTE_ES_LA_MISMA");
        i.Abierta.Should().BeTrue();
    }

    [Fact]
    public void Tambien_se_resuelve_con_la_reapertura_acotada_y_no_se_resuelve_dos_veces()
    {
        var i = Nueva();
        var reapertura = Guid.NewGuid();
        i.ResolverConReapertura(reapertura, null, null, "x", Ahora);
        (i.Resolucion, i.ReaperturaId).Should().Be((ResolucionIncidencia.ReaperturaAcotada, reapertura));
        var otra = () => i.ResolverConPolizaAjuste(Guid.NewGuid(), null, null, "x", Ahora);
        otra.Should().Throw<ConflictException>().Where(e => e.Code == "CONTAB_INCIDENCIA_YA_RESUELTA");
    }
}
