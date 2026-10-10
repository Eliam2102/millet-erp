using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.DevolucionesProveedor;

internal sealed record LineaOrigenDevolucion(Guid? LineaId, Guid ArticuloId, decimal Cantidad);

internal static class DevolucionProveedorOrigenGuard
{
    public static async Task<MovimientoInventario> ValidarAsync(AlmacenDbContext db, IComprasOcReadPort ocPort,
        Guid? recepcionId, Guid proveedorId, IReadOnlyList<LineaOrigenDevolucion> lineas, Guid? excluirId, CancellationToken ct)
    {
        var recepcion = await db.Movimientos.AsNoTracking().Include(m => m.Lineas)
            .SingleOrDefaultAsync(m => m.Id == recepcionId, ct);
        if (recepcion is null || recepcion.Tipo != TipoMovimiento.EntradaCompra || recepcion.Estado != EstadoMovimiento.Registrado || recepcion.OcId is null)
            throw new BusinessRuleException("DEVOLUCION_RECEPCION_INVALIDA", "Selecciona una recepción de compra registrada como origen de la devolución.");
        var oc = await ocPort.ObtenerAsync(recepcion.OcId.Value, ct);
        if (oc is null || oc.ProveedorId != proveedorId)
            throw new BusinessRuleException("DEVOLUCION_PROVEEDOR_INVALIDO", "El proveedor debe ser el de la orden de compra de la recepción original.");
        var previas = await db.Set<DevolucionAProveedor>().AsNoTracking()
            .Where(d => d.RecepcionOrigenId == recepcionId && d.Id != excluirId && d.Estado != EstadoDevolucionProveedor.Rechazada)
            .SelectMany(d => d.Lineas).ToListAsync(ct);
        foreach (var grupo in lineas.GroupBy(l => l.LineaId))
        {
            var origen = recepcion.Lineas.SingleOrDefault(l => l.Id == grupo.Key);
            if (origen is null || grupo.Any(l => l.ArticuloId != origen.ArticuloId))
                throw new BusinessRuleException("DEVOLUCION_LINEA_RECEPCION_INVALIDA", "Cada artículo debe indicar su línea de la recepción original.");
            var devuelto = previas.Where(l => l.LineaRecepcionOrigenId == origen.Id
                || (l.LineaRecepcionOrigenId == null && l.ArticuloId == origen.ArticuloId)).Sum(l => l.Cantidad);
            if (devuelto + grupo.Sum(l => l.Cantidad) > origen.Cantidad)
                throw new BusinessRuleException("DEVOLUCION_EXCEDE_RECEPCION", "La cantidad a devolver, sumada a las devoluciones anteriores, supera la cantidad recibida.");
        }
        return recepcion;
    }
}
