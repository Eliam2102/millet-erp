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
        [new LineaAppRequisicion(Guid.CreateVersion7(), 10m, "PZA", Money.Of(1m, "MXN"), 5, 2440, 3660)]);

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

    [Theory]
    [InlineData(10, 1200, 2600, "M2", 31.2)]   // la app pide hojas: 10 × 3.12 m²
    [InlineData(10, 2440, 3660, "m2", 89.304)]
    [InlineData(10, null, null, "PZA", 10)]     // material por cantidad
    [InlineData(10, 1200, 2600, "PZA", 10)]     // con medida pero unidad por pieza
    public void CantidadDePiezasApp_HojasEnM2YPiezasEnLoDemas(int piezas, int? ancho, int? alto, string unidad, double esperado) =>
        Assert.Equal((decimal)esperado, LineaRequisicion.CantidadDePiezasApp(piezas, ancho, alto, unidad));

    [Fact]
    public void CrearDesdeAlmacenesApp_LlevaElEquipoDeAlmacenEnSusLineas()
    {
        // Jorge (TI), 11-nov: lo que pide la app es del área de Almacén; la OC exige centro de costo en cada línea.
        var equipo = Guid.CreateVersion7();
        var rq = Requisicion.CrearDesdeAlmacenesApp(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Millet.Compras.Domain.Folio.Parse("MID2026-000002"), 2026,
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, Guid.CreateVersion7(), Guid.CreateVersion7(),
            DateTimeOffset.UtcNow, "APP-2", ProveedorApp,
            [new LineaAppRequisicion(Guid.CreateVersion7(), 10m, "M2", Money.Of(1m, "MXN"), 5, 2440, 3660, CentroCostoId: equipo)]);
        Assert.Equal(equipo, Assert.Single(rq.Lineas).CentroCostoId);
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

    // ── M-446: REQUISICION_REDUCIDA / REQUISICION_CANCELADA ───────────────────────────────────

    private static Requisicion RqAppEnSurtido()
    {
        var rq = RqApp();
        rq.RegistrarCubrimiento(rq.Lineas.Select(l => new CubrimientoLinea(l.Id, 0, l.Cantidad)).ToList(), DateTimeOffset.UtcNow);
        return rq;
    }

    [Fact]
    public void QuitarDeCompraApp_BajaLoPorComprarYLoVigente()
    {
        var rq = RqAppEnSurtido();
        rq.QuitarDeCompraApp([(1, 4m)], "App Almacenes: cierre con faltante");

        var l = Assert.Single(rq.Lineas);
        Assert.Equal((6m, 4m, 6m), (l.CantidadDeCompra, l.CantidadSinCompra, l.CantidadVigente));
        Assert.Equal("App Almacenes: cierre con faltante", l.MotivoUltimaLiberacion);
    }

    [Fact]
    public void QuitarDeCompraApp_NoBajaDeLoYaRecibido()
    {
        var rq = RqAppEnSurtido();
        rq.RegistrarRecepcion(rq.Lineas.Single().Id, 8m, DateTimeOffset.UtcNow);
        rq.QuitarDeCompraApp([(1, 5m)], "m");

        var l = rq.Lineas.Single();
        Assert.Equal((8m, 2m), (l.CantidadDeCompra, l.CantidadSinCompra));
    }

    [Fact]
    public void QuitarDeCompraApp_PosicionInexistenteOTerminada_Lanza()
    {
        var rq = RqAppEnSurtido();
        Assert.Equal("RQ_APP_POSICION_INEXISTENTE", Assert.Throws<BusinessRuleException>(() => rq.QuitarDeCompraApp([(2, 1m)], "m")).Code);
        Assert.Equal(10m, rq.Lineas.Single().CantidadDeCompra);

        rq.Cancelar(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow, "App Almacenes");
        Assert.Equal(EstadoRequisicion.Cancelada, rq.Estado);
        Assert.Equal("RQ_APP_REDUCIR_ESTADO", Assert.Throws<BusinessRuleException>(() => rq.QuitarDeCompraApp([(1, 1m)], "m")).Code);
    }
    [Fact]
    public void CrearDesdeAlmacenesApp_ConservaLaPosicionDeLaAppYReducirLaUsa()
    {
        // M-283 (contrato v9): la RQ guarda la posicion de la app; REQUISICION_REDUCIDA se refiere a ella.
        var rq = Requisicion.CrearDesdeAlmacenesApp(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Millet.Compras.Domain.Folio.Parse("MID2026-000003"), 2026,
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, Guid.CreateVersion7(), Guid.CreateVersion7(),
            DateTimeOffset.UtcNow, "RQ-7", ProveedorApp,
            [new LineaAppRequisicion(Guid.CreateVersion7(), 89.304m, "M2", Money.Of(1m, "MXN"), 10, 2440, 3660, Posicion: 3),
             new LineaAppRequisicion(Guid.CreateVersion7(), 4m, "PZA", Money.Of(1m, "MXN"), 4, null, null, Posicion: 1)]);
        Assert.Equal(new short[] { 3, 1 }, rq.Lineas.Select(l => l.Posicion));
        rq.RegistrarCubrimiento(rq.Lineas.Select(l => new CubrimientoLinea(l.Id, 0, l.Cantidad)).ToList(), DateTimeOffset.UtcNow);
        rq.QuitarDeCompraApp([(3, 4m)], "App Almacenes: entrada cancelada");
        // Vidrio en M2: 4 hojas de 2440 × 3660 son 4 × 8.9304 = 35.7216 m².
        Assert.Equal(53.5824m, rq.Lineas.Single(l => l.Posicion == 3).CantidadDeCompra);
        Assert.Equal(4m, rq.Lineas.Single(l => l.Posicion == 1).CantidadDeCompra);
    }

    [Fact]
    public void CrearDesdeAlmacenesApp_PosicionesRepetidasOAMedias_Lanza()
    {
        LineaAppRequisicion L(int? p) => new(Guid.CreateVersion7(), 1m, "PZA", Money.Of(1m, "MXN"), 1, null, null, Posicion: p);
        Requisicion Crear(params LineaAppRequisicion[] ls) => Requisicion.CrearDesdeAlmacenesApp(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Millet.Compras.Domain.Folio.Parse("MID2026-000004"), 2026,
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, Guid.CreateVersion7(), Guid.CreateVersion7(),
            DateTimeOffset.UtcNow, "RQ-8", ProveedorApp, ls);
        Assert.Equal("RQ_APP_POSICIONES_INVALIDAS", Assert.Throws<BusinessRuleException>(() => Crear(L(1), L(1))).Code);
        Assert.Equal("RQ_APP_POSICIONES_INVALIDAS", Assert.Throws<BusinessRuleException>(() => Crear(L(2), L(null))).Code);
    }
}
