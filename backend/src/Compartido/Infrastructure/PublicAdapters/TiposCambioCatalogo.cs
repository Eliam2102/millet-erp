using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Domain.Ports;
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

    /// <summary>
    /// Consulta el rango de tipos de cambio para las <paramref name="monedas"/> solicitadas entre
    /// <paramref name="desde"/> y <paramref name="hasta"/> (ambas inclusive), únicamente de monedas activas (I13.2).
    /// </summary>
    public static async Task<IReadOnlyList<TipoCambioRangoItem>> RangoEnMxnAsync(
        CompartidoDbContext db,
        IReadOnlyList<string> monedas,
        DateOnly desde,
        DateOnly hasta,
        CancellationToken cancellationToken)
    {
        if (monedas is null || monedas.Count == 0)
            return [];

        var codigos = monedas
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        if (codigos.Count == 0)
            return [];

        var items = await (from tc in db.TiposCambio.AsNoTracking()
                           join m in db.Monedas.AsNoTracking() on tc.MonedaId equals m.Id
                           where codigos.Contains(m.Codigo)
                              && m.Activa
                              && tc.Fecha >= desde
                              && tc.Fecha <= hasta
                           orderby tc.Fecha, m.Codigo
                           select new { m.Codigo, tc.Fecha, tc.ValorEnMxn, tc.Origen })
                          .ToListAsync(cancellationToken);

        return items
            .Select(x => new TipoCambioRangoItem(x.Codigo, x.Fecha, x.ValorEnMxn, x.Origen.ToString()))
            .ToList();
    }
}
