using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Calendario;
namespace Millet.CuentasPorPagar.Application.FacturaProveedor.RevisionRepp;
public sealed record RevisarFaltaReppCommand : IRequest<int>;
public sealed class RevisarFaltaReppHandler(CuentasPorPagarDbContext db, IClock clock, ICalendarioHabil calendario, Domain.Ports.Tesoreria.IPagosProveedorReadPort? pagosTesoreria = null,
    Integration.Mappers.PasivoAutorizadoParaPagoMapper? pasivos = null) : IRequestHandler<RevisarFaltaReppCommand, int>
{
    public async Task<int> Handle(RevisarFaltaReppCommand c, CancellationToken cancellationToken)
    {
        var hoy = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var motivo = await db.MotivosRevision.FirstAsync(m => m.Codigo == "FALTA_REPP" && m.Activo, cancellationToken);
        var candidatas = await db.FacturasProveedor.Where(f => f.MetodoPago == "PPD" && f.ImportePagado > 0 && f.Estado != EstadoPasivo.Cancelada).ToListAsync(cancellationToken);
        if (pagosTesoreria is not null && candidatas.Count > 0)
        {
            foreach (var p in await pagosTesoreria.ListarAsync(candidatas.Select(f => f.Id).ToArray(), cancellationToken))
            {
                var local = await db.PagosProveedorLocal.FirstOrDefaultAsync(x => x.EmpresaId == p.EmpresaId && x.PagoId == p.PagoId, cancellationToken);
                if (local is null) { local = new(p.EmpresaId, p.FacturaId, p.PagoId, p.Importe, p.Fecha); db.PagosProveedorLocal.Add(local); }
                else local.Confirmar(p.Importe, p.Fecha);
                local.SincronizarCobertura(p.CubiertoRepp, p.Revertido);
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        foreach (var f in candidatas)
        {
            var pagos = await db.PagosProveedorLocal.Where(p => p.FacturaProveedorId == f.Id && !p.Revertido && p.Importe > 0).ToListAsync(cancellationToken);
            var anterior = f.Estado;
            f.ActualizarReppRecibido(pagos.Count > 0 && pagos.All(p => p.CubiertoRepp >= p.Importe), clock.UtcNow);
            if (anterior == EstadoPasivo.EnRevision && f.Estado == EstadoPasivo.Autorizada && pasivos is not null)
                await pasivos.Handle(new Domain.FacturaProveedor.Events.FacturaProveedorAutorizadaDomainEvent(f.EmpresaId, f.Id, f.OrdenCompraId, clock.UtcNow), cancellationToken);
        }
        var pendientes = await db.PagosProveedorLocal.Where(p => !p.Revertido && p.Importe > p.CubiertoRepp).ToListAsync(cancellationToken);
        var vencidas = new HashSet<Guid>();
        foreach (var pago in pendientes)
            if (await calendario.ContarDiasAsync(pago.FechaPago, hoy, cancellationToken) > 5) vencidas.Add(pago.FacturaProveedorId);
        var facturas = await db.FacturasProveedor.Where(f => vencidas.Contains(f.Id) && f.MetodoPago == "PPD" &&
            (f.Estado == EstadoPasivo.Autorizada || f.Estado == EstadoPasivo.Pagada)).ToListAsync(cancellationToken);
        foreach (var factura in facturas)
            factura.EnviarARevision(motivo.Id, EventListeners.Tesoreria.CancelacionPasivoSolicitadaHandler.DependenciaCxpId, null, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken); return facturas.Count;
    }
}
