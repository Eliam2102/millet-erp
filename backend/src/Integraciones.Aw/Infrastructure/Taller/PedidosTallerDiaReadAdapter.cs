using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Taller;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Integraciones.Aw.Infrastructure.Taller;

/// <summary>
/// Pedidos servidos del taller de una sucursal en un día (E5, Eliam 26-oct): pedidos de A+W en estatus 15 ya ingeridos en
/// Facturación, cuyo último cambio cayó ese día. Solo lectura; vive aquí porque Almacén no referencia Facturación.
/// </summary>
public sealed class PedidosTallerDiaReadAdapter(FacturacionDbContext db) : IPedidosTallerDiaReadPort
{
    // ponytail: día local de Mérida a UTC-6 fijo (sin horario de verano desde 2022); un cambio de zona pide TimeZoneInfo.
    private static readonly TimeSpan Merida = TimeSpan.FromHours(-6);

    public async Task<int> PedidosServidosAsync(Guid sucursalId, DateOnly fecha, CancellationToken ct)
    {
        // Npgsql solo acepta instantes en UTC como parámetro.
        var inicio = new DateTimeOffset(fecha.ToDateTime(TimeOnly.MinValue), Merida).ToUniversalTime();
        var fin = inicio.AddDays(1);
        return await db.PedidosFacturables.AsNoTracking().CountAsync(p => p.SucursalId == sucursalId
            && p.Origen == OrigenPedido.Aw && p.EstadoOrigen == "15" && p.UpdatedAt >= inicio && p.UpdatedAt < fin, ct);
    }
}
