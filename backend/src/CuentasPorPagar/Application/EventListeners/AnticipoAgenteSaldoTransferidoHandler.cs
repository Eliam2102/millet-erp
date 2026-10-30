using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.EventListeners;

// Contrato espejo: CxP no referencia al módulo CE.
public sealed record SaldoAnticipoAgentePayload(Guid EmpresaId, Guid ExpedienteId, string FolioExpediente,
    Guid AnticipoAgenteId, int ResolucionIndice, Guid AgenteId, decimal ImporteMxn, DateOnly Fecha, string Referencia);
public sealed record RecibirSaldoAnticipoAgenteCommand(SaldoAnticipoAgentePayload Payload) : IRequest;
public sealed class AnticipoAgenteSaldoTransferidoHandler(CuentasPorPagarDbContext db, IClock clock)
    : IRequestHandler<RecibirSaldoAnticipoAgenteCommand>
{
    public const string EventType = "comercio_exterior.anticipo-agente.saldo-transferido.v1";
    public async Task Handle(RecibirSaldoAnticipoAgenteCommand request, CancellationToken cancellationToken)
    {
        var p = request.Payload;
        if (p.ExpedienteId == Guid.Empty || p.AnticipoAgenteId == Guid.Empty || p.ResolucionIndice < 0 || string.IsNullOrWhiteSpace(p.Referencia))
            throw new BusinessRuleException("ANTICIPO_ORIGEN_CE_INVALIDO", "El evento requiere expediente, anticipo, índice y referencia.");
        var referencia = $"{p.AnticipoAgenteId}:{p.ResolucionIndice}";
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        // Serializa reentregas concurrentes; el índice único conserva la garantía en BD.
        if (db.Database.IsRelational())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({0xCE140001L})", cancellationToken);
        if (await db.AnticiposProveedor.IgnoreQueryFilters().AnyAsync(a => a.EmpresaId == p.EmpresaId && a.ReferenciaOrigen == referencia, cancellationToken)) return;
        db.AnticiposProveedor.Add(Domain.AnticipoProveedor.AnticipoProveedor.DesdeSaldoComercioExterior(
            p.EmpresaId, p.AgenteId, p.ImporteMxn, p.Fecha, p.FolioExpediente, referencia, clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        if (tx is not null) await tx.CommitAsync(cancellationToken);
    }
}
