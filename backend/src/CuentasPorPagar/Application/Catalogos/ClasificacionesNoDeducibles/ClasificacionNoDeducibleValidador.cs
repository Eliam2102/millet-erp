using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Catalogos.ClasificacionesNoDeducibles;

internal static class ClasificacionNoDeducibleValidador
{
    /// <summary>Devuelve la clave normalizada si existe y está activa; null si viene vacía y no es obligatoria. Si no, lanza <paramref name="codigo"/>.</summary>
    public static async Task<string?> ResolverAsync(CuentasPorPagarDbContext db, string? clave, bool obligatoria, string codigo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clave))
            return obligatoria
                ? throw new BusinessRuleException(codigo, "La clasificación de gasto no deducible es obligatoria.")
                : null;
        var normalizada = clave.Trim().ToUpperInvariant();
        if (!await db.ClasificacionesNoDeducibles.AsNoTracking().AnyAsync(c => c.Clave == normalizada && c.Activa, ct))
            throw new BusinessRuleException(codigo, $"La clasificación '{normalizada}' no existe o está inactiva.");
        return normalizada;
    }
}
