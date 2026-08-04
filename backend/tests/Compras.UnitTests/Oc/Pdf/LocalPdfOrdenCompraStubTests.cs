using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure.Oc.Pdf;

namespace Millet.Compras.UnitTests.Oc.Pdf;

/// <summary>
/// Tests F6-PR1 — stub de PDF placeholder. Verifica que el placeholder
/// incluye folio, totales y agrupación cosmética §3.bis.4.
/// </summary>
public class LocalPdfOrdenCompraStubTests
{
    private static OrdenCompra NewOcConDosLineasMismoArticulo()
    {
        var articuloId = Guid.CreateVersion7();
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Folio.Parse("OC-MID2026-000100"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Manual test");

        oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: articuloId,
            cantidad: 3m, unidadMedida: "PZA", precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7());
        oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: articuloId,
            cantidad: 2m, unidadMedida: "PZA", precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7());
        return oc;
    }

    [Fact]
    public async Task GenerarAsync_IncluyeFolioYTotales()
    {
        var stub = new LocalPdfOrdenCompraStub(NullLogger<LocalPdfOrdenCompraStub>.Instance);
        var oc = NewOcConDosLineasMismoArticulo();

        var resultado = await stub.GenerarAsync(oc, CancellationToken.None);

        var contenido = Encoding.UTF8.GetString(resultado.Contenido);
        Assert.Contains("OC-MID2026-000100", contenido);
        Assert.Contains("TOTAL A PAGAR", contenido);
        Assert.Equal("application/pdf", resultado.ContentType);
        Assert.Equal("OC-MID2026-000100.pdf", resultado.NombreSugerido);
    }

    [Fact]
    public async Task GenerarAsync_AgrupaLineasPorArticulo()
    {
        var stub = new LocalPdfOrdenCompraStub(NullLogger<LocalPdfOrdenCompraStub>.Instance);
        var oc = NewOcConDosLineasMismoArticulo();

        var resultado = await stub.GenerarAsync(oc, CancellationToken.None);

        var contenido = Encoding.UTF8.GetString(resultado.Contenido);
        // 2 líneas con mismo articulo_id → 1 sola fila con qty=5 (3+2).
        // El contenido debe contener "qty=5" exactamente una vez (la línea agrupada).
        var qtyMatches = System.Text.RegularExpressions.Regex.Count(contenido, @"qty=5\b");
        Assert.Equal(1, qtyMatches);
    }
}
