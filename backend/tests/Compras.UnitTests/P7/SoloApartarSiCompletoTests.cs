using Millet.Compras.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
namespace Millet.Compras.UnitTests.P7;

public sealed class SoloApartarSiCompletoTests
{
    private static Requisicion NuevaRq() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Folio.Parse("MID2026-998002"), 2026,
            Clasificacion.MateriaPrima, Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Prioridad.Normal, DateTimeOffset.UtcNow, null);

    [Fact]
    public void Por_omision_es_surtido_parcial_y_se_puede_cambiar_en_borrador()
    {
        var rq = NuevaRq();
        rq.SoloApartarSiCompleto.Should().BeFalse();
        rq.EstablecerSoloApartarSiCompleto(true);
        rq.SoloApartarSiCompleto.Should().BeTrue();
    }

    [Fact]
    public void Se_congela_al_transmitir()
    {
        var rq = NuevaRq();
        rq.AgregarLinea(Guid.NewGuid(), Guid.NewGuid(), 10, "PZA", Money.Mxn(20), centroCostoId: Guid.NewGuid());
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        var ex = Assert.Throws<BusinessRuleException>(() => rq.EstablecerSoloApartarSiCompleto(true));
        ex.Code.Should().Be("RQ_SURTIDO_CONGELADO");
    }
}
