using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Envío a autorización exige PrecioUnitario &gt; 0 en todas las líneas.
/// </summary>
public class OrdenCompraPrecioRequeridoTests
{
    private static OrdenCompra NewOc(params decimal[] precios)
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000060"),
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
        foreach (var precio in precios)
        {
            oc.AgregarLineaManual(
                lineaId: Guid.CreateVersion7(),
                articuloId: Guid.CreateVersion7(),
                cantidad: 1m,
                unidadMedida: "PZA",
                precioUnitario: precio,
                departamentoSolicitanteId: Guid.CreateVersion7());
        }
        return oc;
    }

    [Fact]
    public void EnviarAAutorizacion_ConPrecioCero_LanzaYNombraPosiciones()
    {
        var oc = NewOc(100m, 0m, 50m, 0m);

        var ex = Assert.Throws<BusinessRuleException>(() => oc.EnviarAAutorizacion(DateTimeOffset.UtcNow));

        Assert.Equal("OC_PRECIO_REQUERIDO", ex.Code);
        var posiciones = oc.Lineas.Where(l => l.PrecioUnitario <= 0m).Select(l => l.Posicion).OrderBy(p => p).ToList();
        Assert.Equal(2, posiciones.Count);
        Assert.Contains($"líneas {posiciones[0]}, {posiciones[1]} antes", ex.Message);
    }

    [Fact]
    public void EnviarAAutorizacion_ConTodosLosPreciosPositivos_Pasa()
    {
        var oc = NewOc(100m, 0.01m);

        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);

        Assert.Equal(EstadoOrdenCompra.EnAutorizacionJefeCompras, oc.Estado);
    }
}
