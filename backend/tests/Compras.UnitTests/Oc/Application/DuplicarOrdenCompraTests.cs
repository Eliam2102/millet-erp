using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.UnitTests.Oc.Application;

/// <summary>
/// Tests F6-PR2 — comportamiento del agregado al duplicar (lo crítico
/// que el handler usa: crear OC con OcOrigenId + líneas manuales + sin
/// adjuntos / sin autorizaciones / sub-estados frescos).
/// </summary>
public class DuplicarOrdenCompraTests
{
    [Fact]
    public void DuplicarOc_NuevaTieneOcOrigenId_YEstadoBorrador()
    {
        var origenId = Guid.CreateVersion7();
        var nueva = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000200"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Duplicada de OC-MID2026-000100",
            ocOrigenId: origenId);

        Assert.Equal(origenId, nueva.OcOrigenId);
        Assert.Equal(EstadoOrdenCompra.Borrador, nueva.Estado);
        Assert.Empty(nueva.Adjuntos);
        Assert.Empty(nueva.Autorizaciones);
        Assert.Equal(SubEstadoRecepcion.SinRecepcion, nueva.SubEstadoRecepcion);
        Assert.Equal(SubEstadoFacturacion.SinFactura, nueva.SubEstadoFacturacion);
        Assert.Equal(SubEstadoPago.SinPago, nueva.SubEstadoPago);
    }

    [Fact]
    public void DuplicarOc_AutoReferencia_Lanza()
    {
        var miId = Guid.CreateVersion7();
        var ex = Assert.Throws<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>(() =>
            new OrdenCompra(
                id: miId,
                empresaId: Guid.CreateVersion7(),
                folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000201"),
                folioAnio: 2026,
                proveedorId: Guid.CreateVersion7(),
                sucursalDestinoId: Guid.CreateVersion7(),
                condicionesPagoId: Guid.CreateVersion7(),
                usoPrincipalId: Guid.CreateVersion7(),
                compradorTitularId: Guid.CreateVersion7(),
                encargadoComprasId: Guid.CreateVersion7(),
                fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
                sinRequisicionPrevia: true,
                motivoSinRequisicion: "loop",
                ocOrigenId: miId));
        Assert.Equal("OC_ORIGEN_AUTOREFERENCIA", ex.Code);
    }

    [Fact]
    public void DuplicarOc_LineasComoManuales_NoCopianRequisicionId()
    {
        // Simula el comportamiento del handler: la OC nueva debe recibir
        // las líneas como manuales — sin requisicionId / lineaRequisicionId.
        var nueva = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000202"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Duplicada");

        nueva.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 5m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7());

        var linea = nueva.Lineas.Single();
        Assert.Null(linea.RequisicionId);
        Assert.Null(linea.LineaRequisicionId);
    }
}
