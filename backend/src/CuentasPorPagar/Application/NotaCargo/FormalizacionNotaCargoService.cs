using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.Events;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
using Cargo = Millet.CuentasPorPagar.Domain.NotaCargo.NotaCargo;
using Nc = Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.NotaCreditoProveedor;
namespace Millet.CuentasPorPagar.Application.NotaCargo;
public sealed class FormalizacionNotaCargoService(CuentasPorPagarDbContext db, IPublisher publisher)
{
    public async Task IntentarAsync(Nc nc, DateTimeOffset ahora, CancellationToken ct)
    {
        if (nc.TipoRelacionCfdi != TipoRelacionCfdi.Devolucion || nc.FacturaOrigenId is null || nc.Estado == EstadoNotaCredito.Cancelada || nc.MontoAplicado > 0) return;
        var cargos = await db.NotasCargo.Where(n => n.EmpresaId == nc.EmpresaId && n.FacturaOrigenId == nc.FacturaOrigenId &&
            n.ProveedorId == nc.ProveedorId && n.Moneda == nc.Moneda && n.Monto == nc.Total &&
            n.Estado == EstadoNotaCargo.Aplicada && n.NotaCreditoProveedorId == null).ToListAsync(ct);
        // Un match ambiguo requiere elección explícita; nunca se toma el primero por fecha.
        if (cargos.Count == 1 && !await db.NotasCargo.AnyAsync(n => n.NotaCreditoProveedorId == nc.Id, ct))
            await FormalizarAsync(cargos[0], nc, ahora, ct);
    }
    public async Task FormalizarAsync(Cargo cargo, Nc nc, DateTimeOffset ahora, CancellationToken ct)
    {
        if (nc.TipoRelacionCfdi != TipoRelacionCfdi.Devolucion || nc.Estado == EstadoNotaCredito.Cancelada ||
            nc.ProveedorId != cargo.ProveedorId || nc.EmpresaId != cargo.EmpresaId || nc.Moneda != cargo.Moneda ||
            nc.Total != cargo.Monto || nc.FacturaOrigenId != cargo.FacturaOrigenId || cargo.FacturaOrigenId is null)
            throw new BusinessRuleException("NCG_NC_NO_COINCIDE", "La NC fiscal tipo 03 debe corresponder a la factura, proveedor, moneda e importe de la nota de cargo.");
        if (await db.NotasCargo.AnyAsync(n => n.NotaCreditoProveedorId == nc.Id && n.Id != cargo.Id, ct))
            throw new BusinessRuleException("NCG_NC_YA_VINCULADA", "La NC ya formaliza otra nota de cargo.");
        if (nc.MontoAplicado > 0)
            throw new BusinessRuleException("NCG_NC_YA_APLICADA", "La NC ya fue descontada. Revisa las aplicaciones antes de formalizar el cargo para evitar una deducción doble.");
        cargo.Formalizar(nc.Id, ahora);
        if (cargo.DevolucionAProveedorId is Guid devolucion)
            await publisher.Publish(new NotaCreditoFiscalDevolucionRecibidaDomainEvent(
                nc.EmpresaId, nc.Id, cargo.Id, devolucion, nc.ProveedorId, nc.Total, nc.UuidCfdi, ahora), ct);
    }
}
