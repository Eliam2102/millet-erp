using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.UnitTests.Domain;

public class RequisicionAlmacenesAppTests
{
    private static readonly Guid ProveedorApp = Guid.CreateVersion7();

    private static Requisicion RqApp(string folioExterno = "APP-1") => Requisicion.CrearDesdeAlmacenesApp(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Millet.Compras.Domain.Folio.Parse("MID2026-000001"), 2026,
        Guid.CreateVersion7(), Guid.CreateVersion7(), null, Guid.CreateVersion7(), Guid.CreateVersion7(),
        DateTimeOffset.UtcNow, folioExterno, ProveedorApp,
        [new LineaAppRequisicion(Guid.CreateVersion7(), 10m, "M2", Money.Of(1m, "MXN"), 5, 2440, 3660)]);

    [Fact]
    public void CrearDesdeAlmacenesApp_NaceAutorizadaConProveedorYMedida()
    {
        var rq = RqApp();
        Assert.Equal(EstadoRequisicion.Autorizada, rq.Estado);
        Assert.Equal(OrigenRequisicion.AlmacenesApp, rq.Origen);
        Assert.Equal(ProveedorApp, rq.ProveedorSugeridoId);
        var l = Assert.Single(rq.Lineas);
        Assert.Equal((5, 2440, 3660), (l.Piezas, l.AnchoMm, l.AltoMm));
    }

    [Fact]
    public void ExigirProveedorFijo_ProveedorDistinto_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => RqApp().ExigirProveedorFijo(Guid.CreateVersion7()));
        Assert.Equal("OC_PROVEEDOR_FIJO_APP", ex.Code);
    }

    [Fact]
    public void ExigirProveedorFijo_MismoProveedorOTbd_NoLanza()
    {
        var rq = RqApp();
        rq.ExigirProveedorFijo(ProveedorApp);
        rq.ExigirProveedorFijo(OrdenCompraTbdSentinels.Proveedor);
    }

    [Fact]
    public void ExigirProveedorFijo_RqManual_NoAplica()
    {
        var rq = new Requisicion(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Millet.Compras.Domain.Folio.Parse("MID2026-000002"), 2026, Clasificacion.OrdenCompra,
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Prioridad.Normal, DateTimeOffset.UtcNow, proveedorSugeridoId: ProveedorApp);
        rq.ExigirProveedorFijo(Guid.CreateVersion7());
    }

    [Fact]
    public void CrearDesdeAlmacenesApp_FolioExternoVacioOLargo_Lanza()
    {
        Assert.Throws<BusinessRuleException>(() => RqApp(" "));
        Assert.Throws<BusinessRuleException>(() => RqApp(new string('x', 65)));
    }
}
