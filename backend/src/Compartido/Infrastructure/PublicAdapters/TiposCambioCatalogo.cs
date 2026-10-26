using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>
/// Lectura del catálogo <c>compartido.tipos_cambio</c> que comparten los adaptadores de
/// tipo de cambio de otros módulos (C1.2). Solo el registro de la fecha exacta: si no
/// existe, devuelve <c>null</c> y el consumidor lo reporta; nunca se inventa un TC.
/// </summary>
public static class TiposCambioCatalogo
{
    public const string Mxn = "MXN";

    /// <summary>Equivalente en pesos de 1 unidad de <paramref name="moneda"/> en <paramref name="fecha"/>; MXN vale 1.</summary>
    public static async Task<decimal?> ValorEnMxnAsync(
        CompartidoDbContext db, string moneda, DateOnly fecha, CancellationToken cancellationToken)
    {
        var codigo = moneda.Trim().ToUpperInvariant();
        if (codigo == Mxn) return 1m;
        return await (from tc in db.TiposCambio.AsNoTracking()
                      join m in db.Monedas.AsNoTracking() on tc.MonedaId equals m.Id
                      where m.Codigo == codigo && m.Activa && tc.Fecha == fecha
                      select (decimal?)tc.ValorEnMxn).SingleOrDefaultAsync(cancellationToken);
    }
}
