using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Infrastructure.Persistence;
namespace Millet.ComercioExterior.Application;
public sealed record PagoAnticipoCePayload(Guid EmpresaId, Guid FacturaProveedorId, Guid PagoId, decimal Monto, string? ReferenciaBancaria,
    Guid AnticipoAgenteId, Guid DevolucionId);
public sealed record RecibirPagoAnticipoCeCommand(PagoAnticipoCePayload Pago, bool Devolucion) : IRequest;
public sealed class RecibirPagoAnticipoCeHandler(ComercioExteriorDbContext db) : IRequestHandler<RecibirPagoAnticipoCeCommand>
{
    public async Task Handle(RecibirPagoAnticipoCeCommand request, CancellationToken cancellationToken)
    {
        var p = request.Pago;
        var id = request.Devolucion ? p.AnticipoAgenteId : p.FacturaProveedorId;
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (db.Database.IsRelational())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 19))", cancellationToken);
        var a = await db.AnticiposAgente.SingleOrDefaultAsync(a => a.Id == id && a.EmpresaId == p.EmpresaId, cancellationToken);
        if (a is null || (request.Devolucion ? a.DepositosRecibidos.Contains(p.DevolucionId) : a.PagosRecibidos.Contains(p.PagoId))) return;
        if (request.Devolucion) a.ConfirmarDevolucion(p.DevolucionId, p.Monto, p.ReferenciaBancaria ?? "");
        else a.RegistrarPago(p.PagoId, p.Monto, p.ReferenciaBancaria);
        var e = await db.Expedientes.SingleAsync(e => e.Id == a.ExpedienteId, cancellationToken);
        e.MarcarCambioFinanciero();
        await db.SaveChangesAsync(cancellationToken);
        if (tx is not null) await tx.CommitAsync(cancellationToken);
    }
}
