using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.Elegibilidad;

public sealed class ElegibilidadFacturaService(CuentasPorPagarDbContext db, IComprasOcReadPort compras)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<ResultadoElegibilidad> CalcularAsync(Factura factura, CancellationToken ct, bool considerarRecepcionesLocales = false)
    {
        var neto = factura.Total - factura.NcAplicadasTotal;
        if (factura.Estado == EstadoPasivo.Cancelada) return new(0, 0, 0);
        if (factura.OrdenCompraId is not Guid ocId)
            return new(neto, Math.Max(0, factura.SaldoPendiente), 0);
        var oc = await compras.ObtenerAsync(ocId, ct);
        if (oc is null) return new(0, 0, Math.Max(0, factura.SaldoPendiente));
        await db.Entry(factura).Collection(f => f.Lineas).LoadAsync(ct);
        var disponible = oc.Lineas.ToDictionary(l => l.Id, l => l.CantidadRecibida);
        // Solo al consumir recepción: permite publicar antes de que Compras consuma el mismo evento.
        // Lecturas y pagos usan Compras, que también descuenta devoluciones y cancelaciones.
        if (considerarRecepcionesLocales)
        {
            var recepciones = await db.RecepcionesOcLocal.Where(r => r.OrdenCompraId == ocId).ToListAsync(ct);
            recepciones = recepciones.Concat(db.RecepcionesOcLocal.Local.Where(r => r.OrdenCompraId == ocId))
                .DistinctBy(r => r.RecepcionId).ToList();
            var recibidoLocal = recepciones.SelectMany(r => JsonSerializer.Deserialize<List<LineaRecepcionPayload>>(r.LineasJson, JsonOptions) ?? [])
                .Where(l => l.LineaOcId != null).GroupBy(l => l.LineaOcId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Cantidad));
            disponible = oc.Lineas.ToDictionary(l => l.Id,
                l => Math.Max(l.CantidadRecibida, recibidoLocal.GetValueOrDefault(l.Id)));
        }
        // La metadata de creación no es editable por el proveedor. UUID desempata capturas simultáneas.
        var previas = await db.FacturasProveedor.Include(f => f.Lineas)
            .Where(f => f.OrdenCompraId == ocId && f.Estado != EstadoPasivo.Cancelada &&
                (f.CreatedAt < factura.CreatedAt || (f.CreatedAt == factura.CreatedAt && f.Id.CompareTo(factura.Id) < 0)))
            .ToListAsync(ct);
        foreach (var l in previas.SelectMany(f => f.Lineas).Where(l => l.LineaOcId != null))
            disponible[l.LineaOcId!.Value] = Math.Max(0, disponible.GetValueOrDefault(l.LineaOcId.Value) - l.Cantidad);
        var lineas = factura.Lineas.Select(l => new LineaElegibilidad(l.LineaOcId ?? Guid.Empty, l.Cantidad,
            oc.Lineas.FirstOrDefault(o => o.Id == l.LineaOcId) is { } o ? o.BaseNetaUnitaria ?? o.PrecioUnitario : 0)).ToList();
        return ElegibilidadPago.Calcular(lineas, disponible, neto, factura.AnticipoAplicadoTotal, factura.ImportePagado);
    }
}
