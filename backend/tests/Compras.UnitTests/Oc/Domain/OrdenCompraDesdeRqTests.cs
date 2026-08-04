using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests del método <see cref="OrdenCompra.AgregarLineaDesdeRequisicion"/>
/// (F4-PR2). El handler valida cross-table proveedor/RQ/sucursal; el
/// agregado valida invariantes propias (estado, banderas, IDs).
/// </summary>
public class OrdenCompraDesdeRqTests
{
    private static OrdenCompra NewOcBorradorParaRq() => new(
        id: Guid.CreateVersion7(),
        empresaId: Guid.CreateVersion7(),
        folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000001"),
        folioAnio: 2026,
        proveedorId: Guid.CreateVersion7(),
        sucursalDestinoId: Guid.CreateVersion7(),
        condicionesPagoId: Guid.CreateVersion7(),
        usoPrincipalId: Guid.CreateVersion7(),
        compradorTitularId: Guid.CreateVersion7(),
        encargadoComprasId: Guid.CreateVersion7(),
        fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
        sinRequisicionPrevia: false);

    [Fact]
    public void AgregarLineaDesdeRequisicion_OcBorradorSinRqPreviaFalse_OK()
    {
        var oc = NewOcBorradorParaRq();
        var rqId = Guid.CreateVersion7();
        var lineaRqId = Guid.CreateVersion7();

        var linea = oc.AgregarLineaDesdeRequisicion(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 10m,
            unidadMedida: "PZA",
            precioUnitario: 50m,
            departamentoSolicitanteId: Guid.CreateVersion7(),
            requisicionId: rqId,
            lineaRequisicionId: lineaRqId);

        Assert.Single(oc.Lineas);
        Assert.Equal(rqId, linea.RequisicionId);
        Assert.Equal(lineaRqId, linea.LineaRequisicionId);
    }

    [Fact]
    public void AgregarLineaDesdeRequisicion_OcConSinRqPreviaTrue_Lanza()
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000002"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Test");

        var ex = Assert.Throws<BusinessRuleException>(() => oc.AgregarLineaDesdeRequisicion(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 1m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7(),
            requisicionId: Guid.CreateVersion7(),
            lineaRequisicionId: Guid.CreateVersion7()));
        Assert.Equal("OC_LINEA_DESDE_RQ_INCOMPATIBLE", ex.Code);
    }

    [Fact]
    public void AgregarLineaDesdeRequisicion_RqIdsVacios_Lanza()
    {
        var oc = NewOcBorradorParaRq();
        var ex = Assert.Throws<BusinessRuleException>(() => oc.AgregarLineaDesdeRequisicion(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 1m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7(),
            requisicionId: Guid.Empty,
            lineaRequisicionId: Guid.Empty));
        Assert.Equal("OC_LINEA_RQ_IDS_REQUERIDOS", ex.Code);
    }

    [Fact]
    public void AgregarLineaDesdeRequisicion_HeredaCentroCosto_1a1()
    {
        // Fase E PR3: el CC-Máquina se hereda 1:1 de la línea de RQ. Ambos
        // handlers (CrearOrdenCompraDesdeRequisicion + AgregarLineaDesdeRequisicion)
        // llaman a este mismo método pasando lineaRq.CentroCostoId, así que
        // probar el agregado cubre el camino de propagación compartido.
        var oc = NewOcBorradorParaRq();
        var cc = Guid.CreateVersion7();

        var conCc = oc.AgregarLineaDesdeRequisicion(
            lineaId: Guid.CreateVersion7(), articuloId: Guid.CreateVersion7(), cantidad: 10m,
            unidadMedida: "PZA", precioUnitario: 50m,
            departamentoSolicitanteId: Guid.CreateVersion7(),
            requisicionId: Guid.CreateVersion7(), lineaRequisicionId: Guid.CreateVersion7(),
            centroCostoId: cc);
        Assert.Equal(cc, conCc.CentroCostoId);

        // RQ sin CC → línea de OC sin CC (null se propaga, no se inventa).
        var sinCc = oc.AgregarLineaDesdeRequisicion(
            lineaId: Guid.CreateVersion7(), articuloId: Guid.CreateVersion7(), cantidad: 5m,
            unidadMedida: "PZA", precioUnitario: 20m,
            departamentoSolicitanteId: Guid.CreateVersion7(),
            requisicionId: Guid.CreateVersion7(), lineaRequisicionId: Guid.CreateVersion7(),
            centroCostoId: null);
        Assert.Null(sinCc.CentroCostoId);
    }

    [Fact]
    public void AgregarLineaDesdeRequisicion_MismoArticuloDistintaRq_CreaLineasSeparadas()
    {
        var oc = NewOcBorradorParaRq();
        var articuloId = Guid.CreateVersion7();
        var rqA = Guid.CreateVersion7();
        var rqB = Guid.CreateVersion7();

        oc.AgregarLineaDesdeRequisicion(
            lineaId: Guid.CreateVersion7(), articuloId: articuloId, cantidad: 5m,
            unidadMedida: "PZA", precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7(),
            requisicionId: rqA, lineaRequisicionId: Guid.CreateVersion7());

        oc.AgregarLineaDesdeRequisicion(
            lineaId: Guid.CreateVersion7(), articuloId: articuloId, cantidad: 3m,
            unidadMedida: "PZA", precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7(),
            requisicionId: rqB, lineaRequisicionId: Guid.CreateVersion7());

        // §3.bis.4 — política de preservación: NO se suman.
        Assert.Equal(2, oc.Lineas.Count);
        Assert.Equal(8m, oc.Lineas.Sum(l => l.Cantidad));
        Assert.Equal(2, oc.Lineas.Select(l => l.RequisicionId).Distinct().Count());
    }
}
