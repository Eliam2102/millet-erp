using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests de la gestión de líneas en el agregado <see cref="OrdenCompra"/>
/// (F2-PR1). Cubre <see cref="OrdenCompra.AgregarLineaManual"/> y
/// <see cref="OrdenCompra.RecalcularImpuestos"/>.
/// </summary>
public class OrdenCompraLineasTests
{
    private static OrdenCompra NewOc(bool sinRequisicionPrevia) =>
        new(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Folio.Parse("OC-MID2026-000001"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: sinRequisicionPrevia,
            motivoSinRequisicion: sinRequisicionPrevia ? "Test" : null);

    private static LineaOrdenCompra AgregarLinea(OrdenCompra oc, decimal cantidad = 1m, decimal precio = 100m) =>
        oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: cantidad,
            unidadMedida: "PZA",
            precioUnitario: precio,
            departamentoSolicitanteId: Guid.CreateVersion7());

    [Fact]
    public void AgregarLineaManual_OcSinRqPrevia_OK()
    {
        var oc = NewOc(sinRequisicionPrevia: true);
        var linea = AgregarLinea(oc);

        Assert.Single(oc.Lineas);
        Assert.Equal(1, linea.Posicion);
        Assert.Null(linea.RequisicionId);
        Assert.Null(linea.LineaRequisicionId);
    }

    [Fact]
    public void AgregarLineaManual_ConYSinCentroCosto_AmbosAceptados()
    {
        // Fase E PR3: el comprador elige el CC-Máquina (proxy). Opcional en PR3.
        var oc = NewOc(sinRequisicionPrevia: true);
        var cc = Guid.CreateVersion7();

        var conCc = oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(), articuloId: Guid.CreateVersion7(), cantidad: 1m,
            unidadMedida: "PZA", precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7(),
            centroCostoId: cc);
        Assert.Equal(cc, conCc.CentroCostoId);

        var sinCc = AgregarLinea(oc); // el helper no pasa CC → null
        Assert.Null(sinCc.CentroCostoId);
    }

    [Fact]
    public void AgregarLineaManual_OcConRq_Lanza()
    {
        var oc = NewOc(sinRequisicionPrevia: false);
        var ex = Assert.Throws<BusinessRuleException>(() => AgregarLinea(oc));
        Assert.Equal("OC_LINEA_MANUAL_REQUIERE_SIN_RQ", ex.Code);
    }

    [Fact]
    public void AgregarLineaManual_AsignaPosicionesIncrementales()
    {
        var oc = NewOc(sinRequisicionPrevia: true);
        var l1 = AgregarLinea(oc);
        var l2 = AgregarLinea(oc);
        var l3 = AgregarLinea(oc);

        Assert.Equal(1, l1.Posicion);
        Assert.Equal(2, l2.Posicion);
        Assert.Equal(3, l3.Posicion);
        Assert.Equal(3, oc.Lineas.Count);
    }

    [Fact]
    public void RecalcularImpuestos_AplicaMotorV0ATodasLasLineas()
    {
        var oc = NewOc(sinRequisicionPrevia: true);
        AgregarLinea(oc, cantidad: 1m, precio: 100m);  // subtotal 100, IVA 16
        AgregarLinea(oc, cantidad: 2m, precio: 50m);   // subtotal 100, IVA 16

        oc.RecalcularImpuestos();

        Assert.All(oc.Lineas, l =>
        {
            Assert.Equal(16m, l.IvaImporte);
            Assert.Null(l.RetencionIsr);
        });
    }
}
