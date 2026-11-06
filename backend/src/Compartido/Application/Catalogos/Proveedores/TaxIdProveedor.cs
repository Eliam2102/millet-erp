using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Application.Catalogos.Proveedores;

/// <summary>
/// El Tax ID del proveedor extranjero no se repite, igual que el RFC nacional (86e3yzemu · ADM-proveedor-01) → 409.
/// ponytail: sin índice único porque puede haber duplicados heredados; se valida en alta y edición.
/// </summary>
internal static class TaxIdProveedor
{
#pragma warning disable CA1304, CA1311, CA1862
    public static async Task ValidarUnicoAsync(CompartidoDbContext db, string? taxId, Guid? proveedorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(taxId)) return;
        var normalizado = taxId.Trim().ToLower();
        var duplicado = await db.Proveedores.AsNoTracking()
            .Where(p => p.TaxId != null && p.TaxId.ToLower() == normalizado && p.Id != proveedorId)
            .Select(p => new { p.Clave, p.RazonSocial })
            .FirstOrDefaultAsync(ct);
        if (duplicado is not null)
            throw new ConflictException(
                "PROVEEDOR_TAXID_DUPLICADO",
                $"Ya existe el proveedor {duplicado.Clave} · {duplicado.RazonSocial} con Tax ID {taxId.Trim().ToUpperInvariant()}.");
    }
#pragma warning restore CA1304, CA1311, CA1862
}
