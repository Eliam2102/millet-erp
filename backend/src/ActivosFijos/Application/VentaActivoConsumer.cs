using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.Application;
public sealed class VentaActivoConsumer(ActivosFijosDbContext db, LibroServicio libro, OperacionesLibroServicio operaciones,
    IVentaActivoReadPort ventas, ITransaccionPolizaActivo transaccion)
{
    public async Task ProcesarAsync(Guid factura, CancellationToken ct)
    {
        var venta = await ventas.ObtenerAsync(factura, ct);
        if (venta == null) return;
        if (venta.EmpresaId != libro.Empresa) throw new BusinessRuleException("AF_EMPRESA_INVALIDA", "La venta pertenece a otra empresa.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await libro.BloquearAsync(ct);
        await using var enlace = await transaccion.VincularAsync(ct);
        var op = await db.Set<OperacionLibro>().SingleOrDefaultAsync(x => x.FacturaId == factura, ct);
        // Consulta el estado actual: tolera cancelación recibida antes del evento de timbrado.
        if (venta.Cancelada)
        {
            if (op != null) await operaciones.CancelarVentaAsync(op, ct);
        }
        else if (op == null)
        {
            var a = await db.Activos.Include(x => x.Traslados).SingleAsync(x => x.Numero == venta.ActivoRef, ct);
            var ultimo = await db.Set<CorridaDepreciacion>().Where(x => !x.Anulada).MaxAsync(x => (DateOnly?)x.Periodo, ct);
            var siguiente = ultimo?.AddMonths(1) ?? await libro.PrimerMesAsync(ct) ?? CalculoDepreciacion.Mes(venta.Fecha);
            for (var mes = siguiente; mes < CalculoDepreciacion.Mes(venta.Fecha); mes = mes.AddMonths(1))
                await libro.ConfirmarInternoAsync(mes, false, ct);
            await libro.FechaModificableAsync(venta.Fecha, ct); await libro.PeriodoAsync(venta.Fecha, ct);
            var cfg = await db.Set<ConfiguracionLibro>().SingleOrDefaultAsync(ct) ?? new ConfiguracionLibro();
            op = new() { EmpresaId = a.EmpresaId, ActivoId = a.Id, Tipo = "Venta", Fecha = venta.Fecha, FacturaId = factura, Motivo = "Venta de activo timbrada" };
            await operaciones.BajaAsync(a, op, cfg, venta.Precio, ct); db.Add(op);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
