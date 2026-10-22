using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo de <see cref="IRecepcionesOcReadPort"/>. Lee movimientos
/// Registrados <c>EntradaCompra</c> y <c>SalidaPorDevolucionAProveedor</c> de las OC pedidas.
///
/// <para>
/// Las líneas del movimiento 8.B no traen <c>LineaOcId</c> (solo la cabecera lleva
/// <c>OcId</c> y <c>RecepcionOrigenId</c>). La línea de OC se resuelve por
/// devolución (<c>MovimientoSalidaId</c>) → línea de devolución con la misma
/// posición (el movimiento se crea recorriendo las líneas por <c>Posicion</c>) →
/// <c>LineaRecepcionOrigenId</c> → <c>LineaOcId</c> de la línea de recepción.
/// </para>
///
/// <para>Bypass de empresa, igual que <see cref="AlmacenEntregasReadAdapter"/>.</para>
/// </summary>
public sealed class AlmacenRecepcionesOcReadAdapter : IRecepcionesOcReadPort
{
    private readonly AlmacenDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public AlmacenRecepcionesOcReadAdapter(
        AlmacenDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<IReadOnlyList<RecepcionOcLinea>> ListarPorOcAsync(
        IReadOnlyCollection<Guid> ocIds,
        CancellationToken ct)
    {
        if (ocIds.Count == 0)
        {
            return [];
        }

        using var bypass = _empresaContext.Bypass();
        var ids = ocIds.Distinct().ToArray();

        var filas = await _db.Movimientos
            .AsNoTracking()
            .Where(m => m.Estado == EstadoMovimiento.Registrado
                     && (m.Tipo == TipoMovimiento.EntradaCompra
                         || m.Tipo == TipoMovimiento.SalidaPorDevolucionAProveedor)
                     && m.OcId != null
                     && ids.Contains(m.OcId.Value))
            .SelectMany(m => m.Lineas, (m, l) => new
            {
                MovimientoId = m.Id,
                m.Folio,
                OcId = m.OcId!.Value,
                m.Tipo,
                m.FechaMovimiento,
                l.Posicion,
                l.Cantidad,
                l.LineaOcId,
            })
            .ToListAsync(ct);

        var movDevIds = filas
            .Where(f => f.Tipo == TipoMovimiento.SalidaPorDevolucionAProveedor)
            .Select(f => f.MovimientoId)
            .Distinct()
            .ToArray();

        // (movimiento 8.B, posición) → línea de OC, vía la línea de recepción de origen.
        var lineaOcPorDev = new Dictionary<(Guid, int), Guid>();
        if (movDevIds.Length > 0)
        {
            var devLineas = await _db.DevolucionesProveedor
                .AsNoTracking()
                .Where(d => d.MovimientoSalidaId != null && movDevIds.Contains(d.MovimientoSalidaId.Value))
                .SelectMany(d => d.Lineas, (d, l) => new
                {
                    MovimientoId = d.MovimientoSalidaId!.Value,
                    l.Posicion,
                    l.LineaRecepcionOrigenId,
                })
                .ToListAsync(ct);

            var origenIds = devLineas
                .Where(l => l.LineaRecepcionOrigenId != null)
                .Select(l => l.LineaRecepcionOrigenId!.Value)
                .Distinct()
                .ToArray();

            var lineaOcPorOrigen = await _db.LineasMovimiento
                .AsNoTracking()
                .Where(l => origenIds.Contains(l.Id) && l.LineaOcId != null)
                .ToDictionaryAsync(l => l.Id, l => l.LineaOcId!.Value, ct);

            foreach (var l in devLineas)
            {
                if (l.LineaRecepcionOrigenId is Guid origen && lineaOcPorOrigen.TryGetValue(origen, out var lineaOc))
                {
                    lineaOcPorDev[(l.MovimientoId, l.Posicion)] = lineaOc;
                }
            }
        }

        var resultado = new List<RecepcionOcLinea>(filas.Count);
        foreach (var f in filas)
        {
            var esDevolucion = f.Tipo == TipoMovimiento.SalidaPorDevolucionAProveedor;
            Guid? lineaOcId = esDevolucion
                ? lineaOcPorDev.TryGetValue((f.MovimientoId, f.Posicion), out var lo) ? lo : null
                : f.LineaOcId;
            if (lineaOcId is null)
            {
                continue; // histórico sin enlace a línea de OC: no atribuible
            }

            resultado.Add(new RecepcionOcLinea(
                f.MovimientoId, f.Folio, f.OcId, lineaOcId.Value, f.FechaMovimiento,
                esDevolucion ? -f.Cantidad : f.Cantidad, esDevolucion));
        }

        return resultado;
    }
}
