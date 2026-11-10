using Millet.Almacen.Application.Revision;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.AppAlmacenes;

/// <summary>
/// CAMBIO_ALMACEN de la app (M-446, contrato v8; Jorge 9-nov): las hojas pasan de un almacén a otro de la misma sucursal
/// (cambio de división, de línea o de ambas). Por cada sub-almacén de origen se registra un par enlazado: salida
/// <see cref="TipoMovimiento.TrasladoInterno"/> del origen y entrada <see cref="TipoMovimiento.EntradaTrasladoInterno"/> al
/// destino, con el mismo costo promedio del origen, la misma medida y el mismo lote. No publica evento contable: un traslado
/// no hace póliza. Sin existencia en el origen → <see cref="SalidaAppAlmacenes.EnRevision"/> sin mover nada. El acuse lleva el
/// folio de la salida (la entrada queda ligada a ella). No guarda: el llamador confirma junto con el <c>MensajeApp</c>.
/// </summary>
public static class TrasladoAppAlmacenes
{
    public static async Task<(string Estado, string? Folio, string? Error)> RegistrarAsync(
        AlmacenDbContext db, Guid empresaId, Guid usuarioId, IReadOnlyList<SalidaApp> origen, LlaveAlmacenApp destino,
        CancellationToken ct)
    {
        var (mapa, error) = await LlavesAlmacenApp.ResolverAsync(db, origen[0].SucursalId, [destino], ct);
        if (error is not null) return ("ERROR", null, error);
        if (mapa is null) return ("ERROR", null, "La sucursal no tiene la lista de almacenes del ERP: no hay entre qué almacenes trasladar.");
        if (origen.Any(o => o.Almacen is null))
            return ("ERROR", null, "Falta almacenGeneral.tipo en el origen del cambio de almacén.");
        var destinoUbicacion = mapa[destino].UbicacionId;

        var (partidas, estado, errorReparto) = await SalidaAppAlmacenes.RepartirAsync(db, origen, ct);
        if (partidas is null) return (estado, null, errorReparto);

        var folios = new List<string>();
        var fecha = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var grupo in partidas.GroupBy(p => p.SubAlmacenId))
        {
            var (salida, _) = await MovimientosRevision.AgregarParLiberacionAsync(db, empresaId, usuarioId, fecha, grupo.First().Salida.Motivo,
                grupo.Select(p => new ParLiberacionLinea(p.Linea, destinoUbicacion)).ToList(), ct,
                TipoMovimiento.TrasladoInterno, TipoMovimiento.EntradaTrasladoInterno);
            folios.Add(salida.Folio);
        }
        return (SalidaAppAlmacenes.Recibido, string.Join(", ", folios), null);
    }
}
