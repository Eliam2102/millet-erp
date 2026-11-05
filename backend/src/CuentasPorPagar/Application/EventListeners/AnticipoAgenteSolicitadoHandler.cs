using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
namespace Millet.CuentasPorPagar.Application.EventListeners;
public sealed record SolicitudAnticipoAgentePayload(Guid EmpresaId, Guid ExpedienteId, string FolioExpediente,
    Guid SucursalId, Guid? OrdenCompraId, int Tipo, Guid AnticipoAgenteId, Guid AgenteId, decimal ImporteMxn, DateOnly Fecha, Guid SoporteCalculoId);
public sealed record SolicitarAnticipoAgenteCommand(SolicitudAnticipoAgentePayload Payload) : IRequest;
public sealed class AnticipoAgenteSolicitadoHandler(CuentasPorPagarDbContext db, IIntegrationEventPublisher eventos, IClock clock)
    : IRequestHandler<SolicitarAnticipoAgenteCommand>
{
    public const string EventType = "comercio_exterior.anticipo-agente.solicitado.v1";
    public async Task Handle(SolicitarAnticipoAgenteCommand request, CancellationToken cancellationToken)
    {
        var p = request.Payload;
        if (p.SoporteCalculoId == Guid.Empty || p.ExpedienteId == Guid.Empty || p.SucursalId == Guid.Empty || p.Tipo is not (1 or 2))
            throw new BusinessRuleException("ANTICIPO_ORIGEN_CE_INVALIDO", "La solicitud requiere cálculo de impuestos, expediente y sucursal.");
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (db.Database.IsRelational()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({0xCE190001L})", cancellationToken);
        if (await db.AnticiposProveedor.IgnoreQueryFilters().AnyAsync(a => a.Id == p.AnticipoAgenteId, cancellationToken)) return;
        var a = Domain.AnticipoProveedor.AnticipoProveedor.SolicitarComercioExterior(p.AnticipoAgenteId, p.EmpresaId,
            p.AgenteId, p.ImporteMxn, p.Fecha, p.FolioExpediente, p.OrdenCompraId, clock.UtcNow, p.SucursalId);
        db.AnticiposProveedor.Add(a);
        await eventos.PublishAsync(new PasivoAutorizadoParaPagoIntegrationEvent(p.EmpresaId, clock.UtcNow, a.Id, a.ProveedorId,
            p.OrdenCompraId, p.ImporteMxn, p.ImporteMxn, "MXN", 1, p.Fecha, null, p.FolioExpediente,
            OrigenTipo: "AnticipoAgente", OrigenId: a.Id) { SucursalId = p.SucursalId, TipoOperacionCe = p.Tipo }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (tx is not null) await tx.CommitAsync(cancellationToken);
    }
}
