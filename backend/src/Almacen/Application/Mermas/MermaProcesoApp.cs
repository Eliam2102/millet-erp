using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Mermas;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Mermas;

/// <summary>Una línea de lo que A+W informa: mesa (solo desperdicio), artículo de vidrio ya resuelto, m² y kg.</summary>
public sealed record MermaLineaApp(string? Mesa, Guid ArticuloId, decimal M2, decimal? Kg);

/// <summary>
/// Desperdicio de mesa (varias mesas del día) o merma por etapa (una), ya resuelta a ids del ERP.
/// <c>Etapa</c> y <c>Motivo</c> solo aplican a la merma.
/// </summary>
public sealed record MermaApp(string Tipo, Guid SucursalId, DateOnly Fecha, string? Etapa, string? Motivo, IReadOnlyList<MermaLineaApp> Lineas);

/// <summary>
/// A4.4: registro de la baja de producción en proceso. El desperdicio de mesa entra aplicado y avisa a Contabilidad; la merma
/// entra pendiente y avisa solo cuando se autoriza. No guarda: el llamador hace un solo <c>SaveChangesAsync</c> junto con el
/// <c>MensajeApp</c> (idempotencia por mensajeId).
/// </summary>
public static class MermaProcesoApp
{
    /// <summary>
    /// Costo promedio por m² del vidrio en la sucursal: ponderado por la existencia disponible o, si ya no queda (la hoja
    /// completa se descontó al cortar, A4.3), el del último saldo tocado. <c>null</c> = el artículo nunca tuvo saldo ahí.
    /// </summary>
    public static async Task<decimal?> CostoPromedioM2Async(AlmacenDbContext db, Guid sucursalId, Guid articuloId, CancellationToken ct)
    {
        var saldos = await (from s in db.SaldosInventario
                            join sub in db.SubAlmacenes on s.SubAlmacenId equals sub.Id
                            join alm in db.Almacenes on sub.AlmacenId equals alm.Id
                            where alm.SucursalId == sucursalId && s.ArticuloId == articuloId
                                && sub.Tipo != TipoSubAlmacen.MaterialEnRevision && sub.Tipo != TipoSubAlmacen.Transitorio
                            select new { s.CantidadDisponible, s.CostoPromedioMxn, s.UltimaActualizacionAt }).ToListAsync(ct);
        if (saldos.Count == 0) return null;
        var existencia = saldos.Sum(x => x.CantidadDisponible);
        return existencia > 0
            ? Math.Round(saldos.Sum(x => x.CantidadDisponible * x.CostoPromedioMxn) / existencia, 4)
            : saldos.OrderByDescending(x => x.UltimaActualizacionAt).First().CostoPromedioMxn;
    }

    public static async Task<(string Estado, string? Error)> RegistrarAsync(
        AlmacenDbContext db, IIntegrationEventPublisher events, Guid empresaId, MermaApp m, string? mensajeId, CancellationToken ct)
    {
        var nuevas = new List<MermaProceso>();
        foreach (var l in m.Lineas)
        {
            var costo = await CostoPromedioM2Async(db, m.SucursalId, l.ArticuloId, ct);
            if (costo is null)
                return ("ERROR", $"El artículo {l.ArticuloId} no tiene costo promedio en la sucursal: no se puede valuar.");
            if (m.Tipo == MermaProceso.DesperdicioMesa)
            {
                var mesa = l.Mesa?.Trim().ToUpperInvariant();
                if (await db.MermasProceso.AnyAsync(x => x.Tipo == MermaProceso.DesperdicioMesa && x.SucursalId == m.SucursalId
                        && x.Fecha == m.Fecha && x.Mesa == mesa && x.ArticuloId == l.ArticuloId, ct))
                    return ("ERROR", $"Ya hay desperdicio de la mesa {mesa} del {m.Fecha:yyyy-MM-dd} para ese artículo.");
                nuevas.Add(MermaProceso.DelaMesa(empresaId, m.SucursalId, m.Fecha, l.Mesa ?? "", l.ArticuloId, l.M2, l.Kg, costo.Value, mensajeId));
            }
            else
                nuevas.Add(MermaProceso.PorEtapa(empresaId, m.SucursalId, m.Fecha, m.Etapa ?? "", m.Motivo ?? "", l.ArticuloId,
                    l.Kg ?? throw new BusinessRuleException("MERMA_SIN_KG", "La merma requiere los kilos."), l.M2, costo.Value, mensajeId));
        }
        db.MermasProceso.AddRange(nuevas);
        foreach (var n in nuevas.Where(x => x.Estado == MermaProceso.Aplicada))
            await events.PublishAsync(MermaProcesoAplicadaIntegrationEvent.De(n), ct);
        return ("RECIBIDO", null);
    }

    /// <summary>Autoriza una merma pendiente: queda aplicada y avisa a Contabilidad. Sin guardar.</summary>
    public static async Task AutorizarAsync(IIntegrationEventPublisher events, MermaProceso m, Guid usuarioId, CancellationToken ct)
    {
        m.Autorizar(usuarioId);
        await events.PublishAsync(MermaProcesoAplicadaIntegrationEvent.De(m), ct);
    }
}
