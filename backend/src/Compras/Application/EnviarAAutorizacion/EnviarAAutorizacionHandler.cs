using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.EnviarAAutorizacion;

/// <summary>
/// Handler de <see cref="EnviarAAutorizacionCommand"/>. Carga la
/// requisición con sus líneas, invoca <c>EnviarAAutorizacion</c> en el
/// agregado (que valida estado Borrador y líneas no vacías), persiste
/// y publica el evento <c>RequisicionEnviadaAAutorizacionEvent</c> via
/// MediatR (sin handlers todavía; F4-PR4 los wirea).
/// </summary>
public sealed class EnviarAAutorizacionHandler : IRequestHandler<EnviarAAutorizacionCommand, Unit>
{
    private readonly ComprasDbContext _db;
    private readonly IMediator _mediator;
    private readonly IClock _clock;
    private readonly Millet.CentrosCosto.Application.PublicPorts.IDim3ElegibilidadPort _centros;

    public EnviarAAutorizacionHandler(ComprasDbContext db, IMediator mediator, IClock clock, Millet.CentrosCosto.Application.PublicPorts.IDim3ElegibilidadPort centros)
    {
        _db = db;
        _mediator = mediator;
        _clock = clock;
        _centros = centros;
    }

    public async Task<Unit> Handle(EnviarAAutorizacionCommand command, CancellationToken cancellationToken)
    {
        var requisicion = await _db.Requisiciones
            .Include(r => r.Lineas)
            .FirstOrDefaultAsync(r => r.Id == command.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{command.RequisicionId}' en la empresa actual.");

        foreach (var id in requisicion.Lineas.Select(x => x.CentroCostoId).Distinct())
        {
            if (id is not Guid cc) throw new BusinessRuleException("CECO_INVALIDO", "Todas las líneas deben tener centro de costo antes de transmitir.");
            await CentroCostoLineaGuard.ValidarAsync(_centros, cc, aplicarAlcance: false, cancellationToken);
        }
        var evento = requisicion.EnviarAAutorizacion(_clock.UtcNow);

        // F6-PR3: publicar ANTES de SaveChanges para que el outbox
        // buffer (poblado por mappers de integration) sea drenado por
        // el OutboxSaveChangesInterceptor en la misma TX EF. Antes de
        // F6-PR3 esto era post-save (in-proc only); ahora la atomicidad
        // del outbox lo requiere.
        await _mediator.Publish(evento, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
