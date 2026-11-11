using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.EventListeners;

// Contrato espejo de comercio_exterior.anticipo-agente.comprobacion-revertida.v1 (CxP no referencia al módulo CE).
public sealed record ReversaComprobacionCePayload(Guid EmpresaId, Guid ExpedienteId, string FolioExpediente, Guid SucursalId,
    Guid AnticipoAgenteId, Guid SolicitudId, Guid AgenteId, decimal SaldoAFavorMxn, decimal GastosLiberadosMxn, DateOnly Fecha);
public sealed record RecibirReversaComprobacionCeCommand(ReversaComprobacionCePayload Payload) : IRequest;

/// <summary>
/// M-327: el Contador General autorizó revertir la comprobación de un anticipo al agente aduanal. Igual que el traslado del saldo
/// (<see cref="AnticipoAgenteSaldoTransferidoHandler"/>), todo lo anticipado queda como anticipo amortizable del agente («saldo a
/// favor»), que se compensa contra sus facturas siguientes y nunca queda negativo. Publica la póliza de reversa para Contabilidad.
/// Idempotente por la solicitud de reversa.
/// </summary>
public sealed class AnticipoComprobacionRevertidaHandler(CuentasPorPagarDbContext db, IClock clock, IIntegrationEventPublisher? eventos = null)
    : IRequestHandler<RecibirReversaComprobacionCeCommand>
{
    public const string EventType = "comercio_exterior.anticipo-agente.comprobacion-revertida.v1";
    public static string Referencia(Guid anticipo, Guid solicitud) => $"{anticipo}:reversa:{solicitud}";

    public async Task Handle(RecibirReversaComprobacionCeCommand request, CancellationToken cancellationToken)
    {
        var p = request.Payload;
        if (p.EmpresaId == Guid.Empty || p.ExpedienteId == Guid.Empty || p.AnticipoAgenteId == Guid.Empty || p.SolicitudId == Guid.Empty
            || p.AgenteId == Guid.Empty || p.SaldoAFavorMxn <= 0 || p.GastosLiberadosMxn < 0)
            throw new BusinessRuleException("ANTICIPO_ORIGEN_CE_INVALIDO", "El evento requiere expediente, anticipo, solicitud, agente y saldo positivo.");
        var referencia = Referencia(p.AnticipoAgenteId, p.SolicitudId);
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        // Mismo candado que el traslado del saldo: serializa reentregas; el índice único conserva la garantía en BD.
        if (db.Database.IsRelational())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({0xCE140001L})", cancellationToken);
        if (await db.AnticiposProveedor.IgnoreQueryFilters().AnyAsync(a => a.EmpresaId == p.EmpresaId && a.ReferenciaOrigen == referencia, cancellationToken)) return;
        var saldo = Domain.AnticipoProveedor.AnticipoProveedor.DesdeSaldoComercioExterior(
            p.EmpresaId, p.AgenteId, p.SaldoAFavorMxn, p.Fecha, p.FolioExpediente, referencia, clock.UtcNow);
        db.AnticiposProveedor.Add(saldo);
        if (eventos is not null)
            await eventos.PublishAsync(new AnticipoAgenteComprobacionRevertidaIntegrationEvent(p.EmpresaId, clock.UtcNow, saldo.Id,
                p.AnticipoAgenteId, p.SolicitudId, p.AgenteId, p.SucursalId == Guid.Empty ? null : p.SucursalId, p.SaldoAFavorMxn,
                p.GastosLiberadosMxn, p.Fecha, p.FolioExpediente), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (tx is not null) await tx.CommitAsync(cancellationToken);
    }
}
