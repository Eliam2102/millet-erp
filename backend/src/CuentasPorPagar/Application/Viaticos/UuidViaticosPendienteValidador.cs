using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Viaticos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.Viaticos;

internal static class UuidViaticosPendienteValidador
{
    /// <summary>
    /// UUIDs (mayúsculas; las líneas de viáticos se guardan así) ya comprobados en viáticos con comprobación capturada y aún sin liberar:
    /// su factura se genera hasta liberar, así que <c>FacturasProveedor</c> todavía no los ve (CA8.15).
    /// </summary>
    public static Task<List<string>> BuscarAsync(
        CuentasPorPagarDbContext db, IReadOnlyCollection<string> uuidsMayusculas, Guid excluirSolicitudId, CancellationToken ct) =>
        db.SolicitudesViaticos.AsNoTracking()
            .Where(s => s.Estado == EstadoSolicitudViaticos.ComprobacionCapturada && s.Id != excluirSolicitudId)
            .SelectMany(s => s.Lineas)
            .Where(l => l.UuidCfdi != null && uuidsMayusculas.Contains(l.UuidCfdi))
            .Select(l => l.UuidCfdi!)
            .ToListAsync(ct);
}
