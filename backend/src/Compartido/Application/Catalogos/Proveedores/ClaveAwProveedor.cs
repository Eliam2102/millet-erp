using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Application.Catalogos.Proveedores;

/// <summary>La clave de A+W no se repite entre proveedores (86e3wyjz3 · ALM-APP-1) → 409.</summary>
internal static class ClaveAwProveedor
{
    public static async Task ValidarUnicaAsync(CompartidoDbContext db, int claveAw, Guid? proveedorId, CancellationToken ct)
    {
        var duplicado = await db.Proveedores.AsNoTracking()
            .Where(p => p.ClaveAw == claveAw && p.Id != proveedorId)
            .Select(p => new { p.Clave, p.RazonSocial })
            .FirstOrDefaultAsync(ct);
        if (duplicado is not null)
            throw new ConflictException(
                "PROVEEDOR_CLAVE_AW_DUPLICADA",
                $"Ya existe el proveedor {duplicado.Clave} · {duplicado.RazonSocial} con clave de A+W {claveAw}.");
    }
}
