using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests F5-PR3 — borrador OC mínimo con sentinels TBD para los 3
/// campos críticos de cabecera (proveedor, condiciones de pago, uso
/// principal), bloqueo de transmit y completar cabecera.
/// </summary>
public class OrdenCompraBorradorMinimoTests
{
    private static OrdenCompra NewOcBorradorMinimo() => new(
        id: Guid.CreateVersion7(),
        empresaId: Guid.CreateVersion7(),
        folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000050"),
        folioAnio: 2026,
        proveedorId: OrdenCompraTbdSentinels.Proveedor,
        sucursalDestinoId: Guid.CreateVersion7(),
        condicionesPagoId: OrdenCompraTbdSentinels.CondicionesPago,
        usoPrincipalId: OrdenCompraTbdSentinels.UsoPrincipal,
        compradorTitularId: Guid.CreateVersion7(),
        encargadoComprasId: Guid.CreateVersion7(),
        fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));

    [Fact]
    public void Constructor_ConSentinelsTbd_OcEsBorradorMinimo()
    {
        var oc = NewOcBorradorMinimo();
        Assert.True(oc.EsBorradorMinimo);
    }

    [Fact]
    public void EnviarAAutorizacion_ConBorradorMinimo_Lanza()
    {
        var oc = NewOcBorradorMinimo();
        oc.AgregarLineaDesdeRequisicion(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 1m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7(),
            requisicionId: Guid.CreateVersion7(),
            lineaRequisicionId: Guid.CreateVersion7());

        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.EnviarAAutorizacion(DateTimeOffset.UtcNow));
        Assert.Equal("OC_TRANSMITIR_BORRADOR_MINIMO", ex.Code);
    }

    [Fact]
    public void CompletarCabeceraBorrador_ReemplazaTbds_OcDejaSerBorradorMinimo()
    {
        var oc = NewOcBorradorMinimo();
        var proveedor = Guid.CreateVersion7();
        var condiciones = Guid.CreateVersion7();
        var uso = Guid.CreateVersion7();

        oc.CompletarCabeceraBorrador(
            proveedorId: proveedor,
            condicionesPagoId: condiciones,
            usoPrincipalId: uso);

        Assert.False(oc.EsBorradorMinimo);
        Assert.Equal(proveedor, oc.ProveedorId);
        Assert.Equal(condiciones, oc.CondicionesPagoId);
        Assert.Equal(uso, oc.UsoPrincipalId);
    }

    [Fact]
    public void CompletarCabeceraBorrador_Parcial_MantieneOtrosTbds()
    {
        var oc = NewOcBorradorMinimo();

        oc.CompletarCabeceraBorrador(
            proveedorId: Guid.CreateVersion7(),
            condicionesPagoId: null,
            usoPrincipalId: null);

        Assert.True(oc.EsBorradorMinimo); // sigue con 2 TBDs
        Assert.Equal(OrdenCompraTbdSentinels.CondicionesPago, oc.CondicionesPagoId);
    }

    [Fact]
    public void CompletarCabeceraBorrador_ValorTbd_Lanza()
    {
        var oc = NewOcBorradorMinimo();
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.CompletarCabeceraBorrador(
                proveedorId: OrdenCompraTbdSentinels.Proveedor,
                condicionesPagoId: null,
                usoPrincipalId: null));
        Assert.Equal("OC_COMPLETAR_BORRADOR_VALOR_TBD", ex.Code);
    }

    [Fact]
    public void CompletarCabeceraBorrador_FueraDeBorrador_Lanza()
    {
        // OC normal (sin TBDs) que no es borrador mínimo.
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000051"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "manual");
        oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 1m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7());
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);

        Assert.False(oc.EsBorradorMinimo);
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.CompletarCabeceraBorrador(
                proveedorId: Guid.CreateVersion7(),
                condicionesPagoId: null,
                usoPrincipalId: null));
        Assert.Equal("OC_COMPLETAR_BORRADOR_SOLO_BORRADOR", ex.Code);
    }
}
