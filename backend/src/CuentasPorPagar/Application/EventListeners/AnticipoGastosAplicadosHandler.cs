using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.FacturaProveedor.AplicarAnticipo;
using Millet.CuentasPorPagar.Application.Integration.Mappers;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.CuentasPorPagar.Application.EventListeners;
public sealed record GastoAnticipoCe(Guid GastoId, Guid FacturaProveedorId, decimal Importe);
public sealed record GastosAnticipoCePayload(Guid EmpresaId, Guid AnticipoAgenteId, Guid AgenteId,
    string FolioExpediente, int Revision, IReadOnlyList<GastoAnticipoCe> Gastos);
public sealed record AplicarGastosAnticipoCeCommand(GastosAnticipoCePayload Payload) : IRequest;
public sealed class AnticipoGastosAplicadosHandler(CuentasPorPagarDbContext db, IClock clock,
    AplicarAnticipoAFacturaHandler aplicar, Millet.CuentasPorPagar.Application.PublicPorts.IPagosFacturaCeReadPort pagosReales, PasivoAutorizadoParaPagoMapper pasivos, FacturaPagoAplicadoMapper pagos)
    : IRequestHandler<AplicarGastosAnticipoCeCommand>
{
    public const string EventType = "comercio_exterior.anticipo-agente.gastos-aplicados.v1";
    public async Task Handle(AplicarGastosAnticipoCeCommand request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var p = request.Payload;
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (tx is not null) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({0x50335F5041474FL})", ct);
        var estado = await db.Set<ComprobacionAnticipoCe>().SingleOrDefaultAsync(x => x.Id == p.AnticipoAgenteId && x.EmpresaId == p.EmpresaId, ct);
        if (estado is not null && estado.Revision >= p.Revision) return;
        estado ??= new(p.AnticipoAgenteId, p.EmpresaId);
        if (db.Entry(estado).State == EntityState.Detached) db.Add(estado);
        var prefijo = $"{p.AnticipoAgenteId}:gasto:";
        var anteriores = await db.AnticiposProveedor.Where(a => a.EmpresaId == p.EmpresaId && a.ReferenciaOrigen != null && a.ReferenciaOrigen.StartsWith(prefijo)).ToListAsync(ct);
        var ids = anteriores.Select(a => a.Id).ToArray();
        var facturaIds = p.Gastos.Select(g => g.FacturaProveedorId).ToArray();
        var facturas = await db.FacturasProveedor.Include(f => f.Movimientos)
            .Where(f => f.EmpresaId == p.EmpresaId && (facturaIds.Contains(f.Id) || f.Movimientos.Any(m => m.DocumentoId != null && ids.Contains(m.DocumentoId.Value)))).ToListAsync(ct);
        // Valida la fotografía completa antes de deshacer aplicaciones anteriores.
        var errores = new List<string>();
        foreach (var g in p.Gastos)
        {
            var f = facturas.SingleOrDefault(f => f.Id == g.FacturaProveedorId);
            var recuperable = f?.Movimientos.Where(m => m.DocumentoId != null && ids.Contains(m.DocumentoId.Value) && m.Tipo == TipoMovimientoPasivo.Anticipo).Sum(m => m.Monto) ?? 0;
            if (await pagosReales.UltimoPagoAsync(g.FacturaProveedorId, ct) is not null || f is null || f.ProveedorId != p.AgenteId || f.ImportePagado > 0 || f.Estado == EstadoPasivo.Cancelada || g.Importe <= 0 || f.SaldoPendiente + recuperable < g.Importe)
                errores.Add($"Factura {f?.FolioProveedor ?? g.FacturaProveedorId.ToString()}: no aplicada en Cuentas por Pagar; revise pagos y saldo.");
        }
        if (errores.Count > 0)
        {
            estado.Error = string.Join(" ", errores); estado.Revision = p.Revision;
            await db.SaveChangesAsync(ct); if (tx is not null) await tx.CommitAsync(ct); return;
        }
        foreach (var a in anteriores)
        {
            foreach (var f in facturas.Where(f => f.Movimientos.Any(m => m.DocumentoId == a.Id)))
            {
                f.RevertirAnticipo(a.Id, clock.HoyLocal());
                if (f.Estado == EstadoPasivo.Autorizada)
                    await pasivos.Handle(new(f.EmpresaId, f.Id, f.OrdenCompraId, clock.UtcNow), ct);
                await pagos.Handle(new(f.EmpresaId, f.Id, f.OrdenCompraId, f.ImporteSaldado, clock.UtcNow, f.Movimientos.Last().Id), ct);
            }
            a.ReabrirAplicacionCe(a.MontoEntregado);
            a.Cancelar("Corrección de comprobación de Comercio Exterior", clock.UtcNow);
        }
        await db.SaveChangesAsync(ct);
        foreach (var g in p.Gastos)
        {
            var referencia = prefijo + g.GastoId;
            var a = anteriores.SingleOrDefault(a => a.ReferenciaOrigen == referencia);
            if (a is null)
            {
                a = Domain.AnticipoProveedor.AnticipoProveedor.DesdeGastoComercioExterior(p.EmpresaId, p.AgenteId, g.Importe, clock.HoyLocal(), p.FolioExpediente, referencia, clock.UtcNow, facturas.Single(f => f.Id == g.FacturaProveedorId).Moneda, facturas.Single(f => f.Id == g.FacturaProveedorId).TipoCambio);
                db.AnticiposProveedor.Add(a);
            }
            else a.ReabrirAplicacionCe(g.Importe);
            await db.SaveChangesAsync(ct);
            var f = facturas.Single(f => f.Id == g.FacturaProveedorId);
            await aplicar.Handle(new(f.Id, f.Version, a.Id, a.Version, g.Importe), ct);
        }
        estado.Error = null; estado.Revision = p.Revision;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }
}
