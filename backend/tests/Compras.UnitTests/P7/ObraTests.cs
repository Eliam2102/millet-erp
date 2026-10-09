using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
namespace Millet.Compras.UnitTests.P7;
public sealed class ObraTests
{
    [Theory]
    [InlineData(null, null)] [InlineData("  DEMO Obra P7  ", "DEMO Obra P7")]
    public void Rq_conserva_obra_opcional_y_se_congela_al_transmitir(string? captura, string? esperado)
    {
        var rq = new Requisicion(Guid.NewGuid(), Guid.NewGuid(), Millet.Compras.Domain.Folio.Parse("MID2026-998001"), 2026,
            Clasificacion.MateriaPrima, Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Prioridad.Normal, DateTimeOffset.UtcNow, null);
        rq.AsignarObra(captura);
        rq.Obra.Should().Be(esperado);
        rq.AgregarLinea(Guid.NewGuid(), Guid.NewGuid(), 10, "PZA", Money.Mxn(20), centroCostoId: Guid.NewGuid());
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        Assert.Throws<BusinessRuleException>(() => rq.AsignarObra("Otra"));
    }
    [Fact]
    public void Oc_no_consolida_obras_distintas()
    {
        var oc = new OrdenCompra(Guid.NewGuid(), Guid.NewGuid(), Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-998001"), 2026,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 9));
        oc.HeredarObra("DEMO A");
        oc.AgregarLineaDesdeRequisicion(Guid.NewGuid(), Guid.NewGuid(), 10, "PZA", 20, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), centroCostoId: Guid.NewGuid());
        oc.HeredarObra("DEMO A");
        var ex = Assert.Throws<BusinessRuleException>(() => oc.HeredarObra("DEMO B"));
        ex.Code.Should().Be("OC_OBRAS_DISTINTAS");
        oc.Obra.Should().Be("DEMO A");
    }
    [Fact]
    public void Apartado_activo_por_defecto_y_se_puede_apagar()
    {
        var settings = ComprasSettings.CrearDefault(Guid.NewGuid());
        settings.ApartarExistenciaAlAutorizar.Should().BeTrue();
        settings.EstablecerApartarExistenciaAlAutorizar(false);
        settings.ApartarExistenciaAlAutorizar.Should().BeFalse();
    }
}
