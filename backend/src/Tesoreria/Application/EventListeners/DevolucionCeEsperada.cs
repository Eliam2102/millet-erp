using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Infrastructure.Persistence;
namespace Millet.Tesoreria.Application.EventListeners;
public sealed record DevolucionCePayload(Guid EmpresaId, Guid SucursalId, Guid DevolucionId, Guid AnticipoAgenteId,
    Guid AgenteId, decimal ImporteMxn, string FolioExpediente, Guid? RegistradoPor = null, DateOnly? FechaComprobacion = null);
public sealed record RecibirDevolucionCeCommand(DevolucionCePayload Devolucion) : IRequest;
public sealed class RecibirDevolucionCeHandler(TesoreriaDbContext db) : IRequestHandler<RecibirDevolucionCeCommand>
{
    public const string EventType = "comercio_exterior.anticipo-agente.devolucion-aprobada.v1";
    public async Task Handle(RecibirDevolucionCeCommand request, CancellationToken cancellationToken)
    {
        var p = request.Devolucion;
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (db.Database.IsRelational()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({0xCE190002L})", cancellationToken);
        if (await db.DepositosConfirmacion.IgnoreQueryFilters().AnyAsync(d => d.DevolucionCeId == p.DevolucionId, cancellationToken)) return;
        db.DepositosConfirmacion.Add(DepositoConfirmacion.CrearExpectativaCe(p.EmpresaId, p.SucursalId, p.DevolucionId,
            p.AnticipoAgenteId, p.AgenteId, p.ImporteMxn, p.FolioExpediente, p.RegistradoPor, p.FechaComprobacion));
        await db.SaveChangesAsync(cancellationToken);
        if (tx is not null) await tx.CommitAsync(cancellationToken);
    }
}
