using Millet.Compras.Domain.Oc;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests del comportamiento de liberación de RQ desde el agregado (F4-PR3):
/// <list type="bullet">
///   <item><see cref="OrdenCompra.EliminarLinea"/> devuelve la RQ a liberar
///         solo si era la última línea con esa FK.</item>
///   <item><see cref="OrdenCompra.Cancelar"/> devuelve todas las RQs únicas
///         comprometidas al momento de la cancelación.</item>
/// </list>
/// El handler es quien aplica <c>Requisicion.LiberarDeOc()</c> + publica los
/// eventos; el agregado solo expone el contrato.
/// </summary>
public class OrdenCompraLiberacionRqTests
{
    private static OrdenCompra NewOcBorradorParaRq() => new(
        id: Guid.CreateVersion7(),
        empresaId: Guid.CreateVersion7(),
        folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000010"),
        folioAnio: 2026,
        proveedorId: Guid.CreateVersion7(),
        sucursalDestinoId: Guid.CreateVersion7(),
        condicionesPagoId: Guid.CreateVersion7(),
        usoPrincipalId: Guid.CreateVersion7(),
        compradorTitularId: Guid.CreateVersion7(),
        encargadoComprasId: Guid.CreateVersion7(),
        fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
        sinRequisicionPrevia: false);

    private static Guid AgregarLineaRq(OrdenCompra oc, Guid rqId)
    {
        var lineaId = Guid.CreateVersion7();
        oc.AgregarLineaDesdeRequisicion(
            lineaId: lineaId,
            articuloId: Guid.CreateVersion7(),
            cantidad: 1m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7(),
            requisicionId: rqId,
            lineaRequisicionId: Guid.CreateVersion7());
        return lineaId;
    }

    [Fact]
    public void EliminarLinea_UltimaLineaDeLaRq_DevuelveRqALiberar()
    {
        var oc = NewOcBorradorParaRq();
        var rqId = Guid.CreateVersion7();
        var lineaId = AgregarLineaRq(oc, rqId);

        var resultado = oc.EliminarLinea(lineaId);

        Assert.Equal(rqId, resultado.RequisicionIdALiberar);
        Assert.Empty(oc.Lineas);
    }

    [Fact]
    public void EliminarLinea_QuedanOtrasLineasDeLaMismaRq_NoLibera()
    {
        var oc = NewOcBorradorParaRq();
        var rqId = Guid.CreateVersion7();
        var lineaA = AgregarLineaRq(oc, rqId);
        _ = AgregarLineaRq(oc, rqId);

        var resultado = oc.EliminarLinea(lineaA);

        Assert.Null(resultado.RequisicionIdALiberar);
        Assert.Single(oc.Lineas);
    }

    [Fact]
    public void EliminarLinea_LineaManualSinRq_NoLibera()
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000011"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Manual");

        var lineaId = Guid.CreateVersion7();
        oc.AgregarLineaManual(
            lineaId: lineaId,
            articuloId: Guid.CreateVersion7(),
            cantidad: 1m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7());

        var resultado = oc.EliminarLinea(lineaId);

        Assert.Null(resultado.RequisicionIdALiberar);
    }

    [Fact]
    public void Cancelar_OcConTresRqs_DevuelveTresRqsUnicas()
    {
        var oc = NewOcBorradorParaRq();
        var rqA = Guid.CreateVersion7();
        var rqB = Guid.CreateVersion7();
        var rqC = Guid.CreateVersion7();
        AgregarLineaRq(oc, rqA);
        AgregarLineaRq(oc, rqA); // 2 líneas de la misma RQ
        AgregarLineaRq(oc, rqB);
        AgregarLineaRq(oc, rqC);

        var resultado = oc.Cancelar(
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoCancelacionId: Guid.CreateVersion7(),
            motivoCancelacionTexto: "Test");

        Assert.Equal(3, resultado.RequisicionesALiberar.Count);
        Assert.Contains(rqA, resultado.RequisicionesALiberar);
        Assert.Contains(rqB, resultado.RequisicionesALiberar);
        Assert.Contains(rqC, resultado.RequisicionesALiberar);
    }

    [Fact]
    public void Cancelar_OcSinLineas_DevuelveListaVacia()
    {
        var oc = NewOcBorradorParaRq();

        var resultado = oc.Cancelar(
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoCancelacionId: Guid.CreateVersion7());

        Assert.Empty(resultado.RequisicionesALiberar);
    }
}
