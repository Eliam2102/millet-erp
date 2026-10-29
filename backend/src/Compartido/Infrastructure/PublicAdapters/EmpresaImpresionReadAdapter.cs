using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Almacen.Domain.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Documentos;
using Millet.SharedKernel.Application.Blob;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter de <see cref="IEmpresaImpresionReadPort"/>: datos vigentes de la empresa y su logo,
/// igual que el encabezado de la OC (FMT-2). Si el logo no se puede leer se imprime el nombre.
/// </summary>
public sealed class EmpresaImpresionReadAdapter(
    CompartidoDbContext db, IBlobStoragePort blobs, ILogger<EmpresaImpresionReadAdapter> logger)
    : IEmpresaImpresionReadPort
{
    public async Task<EmpresaEncabezado?> ObtenerAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var e = await db.Empresas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == empresaId, cancellationToken);
        if (e is null) return null;
        return new EmpresaEncabezado(e.RazonSocial, e.NombreComercial, e.Rfc, e.RegimenFiscal,
            PdfDomicilio.Lineas(e.Calle, e.NumeroExterior, e.NumeroInterior, e.Colonia, e.Ciudad,
                e.Municipio, e.Estado, e.CodigoPostal, e.Pais),
            await LogoAsync(e.LogoBlobRef, cancellationToken));
    }

    private async Task<byte[]?> LogoAsync(string? blobRef, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(blobRef)) return null;
        try
        {
            await using var stream = await blobs.ObtenerStreamAsync(blobRef, cancellationToken);
            using var memoria = new MemoryStream();
            await stream.CopyToAsync(memoria, cancellationToken);
            return memoria.ToArray();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo leer el logo de la empresa para un documento de Almacén.");
            return null;
        }
    }
}
