using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Listar;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Application.Oc;

/// <summary>Conteo de OC vigentes en la ventana de recurrencia por proveedor y comprador titular.</summary>
internal sealed record ConteoRecurrenciaOc(Guid ProveedorId, Guid CompradorTitularId, int Conteo);

/// <summary>
/// Regla única de "OC recurrente": mismo proveedor y mismo comprador titular con
/// al menos el mínimo de OC no canceladas en la ventana de recurrencia. La propia
/// OC cuenta (no se excluye). El proveedor TBD (sentinela) nunca es recurrente. La usan la bandeja de pendientes y el detalle.
/// </summary>
internal static class RecurrenciaOc
{
    public static DateOnly Desde(DateTimeOffset ahoraUtc) =>
        DateOnly.FromDateTime(ahoraUtc.UtcDateTime).AddDays(-PendientesAutorizacionClasificacion.VentanaRecurrenciaDias);

    public static Task<List<ConteoRecurrenciaOc>> ContarAsync(
        ComprasDbContext db, IReadOnlyCollection<Guid> proveedorIds, DateOnly desde, CancellationToken ct) =>
        db.OrdenesCompra
            .AsNoTracking()
            .Where(o => proveedorIds.Contains(o.ProveedorId)
                        && o.FechaDocumento >= desde
                        && o.Estado != EstadoOrdenCompra.Cancelada)
            .GroupBy(o => new { o.ProveedorId, o.CompradorTitularId })
            .Select(g => new ConteoRecurrenciaOc(g.Key.ProveedorId, g.Key.CompradorTitularId, g.Count()))
            .ToListAsync(ct);

    public static bool EsRecurrente(IEnumerable<ConteoRecurrenciaOc> conteos, Guid proveedorId, Guid compradorId) =>
        proveedorId != OrdenCompraTbdSentinels.Proveedor
        && conteos.Any(c => c.ProveedorId == proveedorId
                         && c.CompradorTitularId == compradorId
                         && PendientesAutorizacionClasificacion.EsRecurrente(c.Conteo));
}
