using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.ProductoTerminado.Infrastructure;

public sealed class PtScanHandler(ProductoTerminadoDbContext db, IPedidosPtReadPort orders, IClock clock,
    IOptions<PtScanOptions> options) : IRequestHandler<RegistrarEscaneoPieza, ScanResult>
{
    public async Task<ScanResult> Handle(RegistrarEscaneoPieza request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        await using var tx = await BeginAsync(request.EmpresaId, ct);
        var existing = await db.Scans.SingleOrDefaultAsync(x => x.EmpresaId == request.EmpresaId && x.Origen == request.Origin && x.OrigenEventoId == request.SourceEventId, ct);
        if (existing is not null) return existing.ToResult();
        var scan = new PtScan(request, clock.UtcNow); db.Scans.Add(scan);
        await ApplyAsync(scan, ct);
        await db.SaveChangesAsync(ct); if (tx is not null) await tx.CommitAsync(ct);
        return scan.ToResult();
    }
    public async Task<IDbContextTransaction?> BeginAsync(Guid empresaId, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        var tx = await db.Database.BeginTransactionAsync(ct);
        // Comparte el candado entre HTTP y worker: dos eventos concurrentes no crean dos altas.
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"millet:pt:scan:" + empresaId}, 0))", ct);
            return tx;
        }
        catch { await tx.DisposeAsync(); throw; }
    }
    public async Task ApplyAsync(PtScan scan, CancellationToken ct)
    {
        var source = await orders.GetAsync(scan.EmpresaId, scan.NumeroPedido, ct);
        var reason = scan.Validate(source); scan.Attempt(reason, clock.UtcNow, options.Value.PospuestoHoras);
        if (reason is not null) return;
        var order = await db.Orders.SingleOrDefaultAsync(x => x.EmpresaId == scan.EmpresaId && x.NumeroPedido == scan.NumeroPedido, ct);
        if (order is null) { order = new(scan.EmpresaId, source!); db.Orders.Add(order); }
        var positions = await db.Positions.Where(x => x.PedidoPtId == order.Id).ToListAsync(ct);
        order.Refresh(source!);
        foreach (var line in source!.Lines)
        {
            var position = positions.SingleOrDefault(x => x.Posicion == line.Position);
            if (position is null) db.Positions.Add(new(scan.EmpresaId, order.Id, line)); else position.Refresh(line);
        }
        foreach (var removed in positions.Where(x => !source.Lines.Any(l => l.Position == x.Posicion)))
            removed.Refresh(new(removed.Posicion, removed.ProductoId, removed.Descripcion, 0, null));
        var piece = await db.Pieces.SingleOrDefaultAsync(x => x.PedidoPtId == order.Id && x.Posicion == scan.Posicion && x.NumeroPieza == scan.NumeroPieza, ct);
        if (piece is not null) { scan.Complete(piece.Id, true); return; }
        piece = new(scan, order.Id); db.Pieces.Add(piece); db.Movements.Add(new(piece, scan.UsuarioAw)); scan.Complete(piece.Id, false);
        var ev = new PiezaIngresadaEvent(scan.EmpresaId, piece.Id, scan.NumeroPedido, scan.Posicion,
            source.Lines.Single(x => x.Position == scan.Posicion).ProductId, piece.SucursalId, piece.EscaneadaAt);
        // Escritura explícita en el outbox propio: no depende del fallback de rutas de otros módulos.
        db.OutboxEntries.Add(new(Guid.CreateVersion7(), ev.EventType, JsonSerializer.Serialize(ev), ev.OcurridoEn, ev.EmpresaId));
    }
}
