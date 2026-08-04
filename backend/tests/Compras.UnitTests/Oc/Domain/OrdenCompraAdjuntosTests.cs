using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests de gestión de adjuntos en el agregado <see cref="OrdenCompra"/>
/// (F2-PR4): <see cref="OrdenCompra.AdjuntarDocumento"/> y
/// <see cref="OrdenCompra.RemoverAdjunto"/>.
/// </summary>
public class OrdenCompraAdjuntosTests
{
    private static OrdenCompra NewOcBorrador() => new(
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
        fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));

    private static AdjuntoOC Agregar(OrdenCompra oc) =>
        oc.AdjuntarDocumento(
            adjuntoId: Guid.CreateVersion7(),
            tipoDocumentoId: Guid.CreateVersion7(),
            nombreArchivo: "cotizacion.pdf",
            blobUrl: "file:///tmp/oc-blobs/abc.pdf",
            contentType: "application/pdf",
            tamañoBytes: 1024,
            fechaCarga: DateTimeOffset.UtcNow,
            usuarioCargaId: Guid.CreateVersion7());

    [Fact]
    public void AdjuntarDocumento_EnBorrador_OK()
    {
        var oc = NewOcBorrador();
        var adjunto = Agregar(oc);

        Assert.Single(oc.Adjuntos);
        Assert.Equal("cotizacion.pdf", adjunto.NombreArchivo);
        Assert.Equal(1024, adjunto.TamañoBytes);
    }

    [Fact]
    public void AdjuntarDocumento_NombreVacio_Lanza()
    {
        var oc = NewOcBorrador();
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.AdjuntarDocumento(
                adjuntoId: Guid.CreateVersion7(),
                tipoDocumentoId: Guid.CreateVersion7(),
                nombreArchivo: "",
                blobUrl: "file:///tmp/x.pdf",
                contentType: "application/pdf",
                tamañoBytes: 100,
                fechaCarga: DateTimeOffset.UtcNow,
                usuarioCargaId: Guid.CreateVersion7()));
        Assert.Equal("ADJUNTO_NOMBRE_INVALIDO", ex.Code);
    }

    [Fact]
    public void AdjuntarDocumento_BlobUrlVacia_Lanza()
    {
        var oc = NewOcBorrador();
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.AdjuntarDocumento(
                adjuntoId: Guid.CreateVersion7(),
                tipoDocumentoId: Guid.CreateVersion7(),
                nombreArchivo: "x.pdf",
                blobUrl: "",
                contentType: "application/pdf",
                tamañoBytes: 100,
                fechaCarga: DateTimeOffset.UtcNow,
                usuarioCargaId: Guid.CreateVersion7()));
        Assert.Equal("ADJUNTO_BLOB_URL_REQUERIDA", ex.Code);
    }

    [Fact]
    public void AdjuntarDocumento_TamanoCero_Lanza()
    {
        var oc = NewOcBorrador();
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.AdjuntarDocumento(
                adjuntoId: Guid.CreateVersion7(),
                tipoDocumentoId: Guid.CreateVersion7(),
                nombreArchivo: "x.pdf",
                blobUrl: "file:///tmp/x.pdf",
                contentType: "application/pdf",
                tamañoBytes: 0,
                fechaCarga: DateTimeOffset.UtcNow,
                usuarioCargaId: Guid.CreateVersion7()));
        Assert.Equal("ADJUNTO_TAMANO_INVALIDO", ex.Code);
    }

    [Fact]
    public void RemoverAdjunto_EnBorrador_OK_DevuelveBlobUrl()
    {
        var oc = NewOcBorrador();
        var adjunto = Agregar(oc);

        var blobUrl = oc.RemoverAdjunto(adjunto.Id);

        Assert.Empty(oc.Adjuntos);
        Assert.Equal("file:///tmp/oc-blobs/abc.pdf", blobUrl);
    }

    [Fact]
    public void RemoverAdjunto_Inexistente_Lanza()
    {
        var oc = NewOcBorrador();
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.RemoverAdjunto(Guid.CreateVersion7()));
        Assert.Equal("OC_ADJUNTO_NO_ENCONTRADO", ex.Code);
    }

    [Fact]
    public void Adjuntos_BuscarPorAggregateRootId_DevuelveOrdenCompraId()
    {
        var oc = NewOcBorrador();
        var adjunto = Agregar(oc);
        Assert.Equal(oc.Id, adjunto.AggregateRootId);
    }
}
