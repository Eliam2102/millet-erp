using Microsoft.Extensions.Logging.Abstractions;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure.Oc.Pdf;

namespace Millet.Compras.UnitTests.Oc.Pdf;

/// <summary>
/// Smoke tests F6-PR3 — el generador QuestPDF produce bytes con
/// header PDF válido (`%PDF-1.X`).
/// </summary>
public class QuestPdfOrdenCompraGeneratorTests
{
    private static OrdenCompra NewOcMinima()
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Folio.Parse("OC-MID2026-000300"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Test PDF");
        oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 2m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7());
        return oc;
    }

    [Fact]
    public async Task GenerarAsync_ProduceBytesConHeaderPdf()
    {
        var generator = new QuestPdfOrdenCompraGenerator(
            NullLogger<QuestPdfOrdenCompraGenerator>.Instance);
        var oc = NewOcMinima();

        var resultado = await generator.GenerarAsync(oc, CancellationToken.None);

        Assert.Equal("application/pdf", resultado.ContentType);
        Assert.Equal("OC-MID2026-000300.pdf", resultado.NombreSugerido);
        // Header de PDF: bytes "%PDF-".
        Assert.True(resultado.Contenido.Length > 100);
        Assert.Equal((byte)'%', resultado.Contenido[0]);
        Assert.Equal((byte)'P', resultado.Contenido[1]);
        Assert.Equal((byte)'D', resultado.Contenido[2]);
        Assert.Equal((byte)'F', resultado.Contenido[3]);
    }
}
