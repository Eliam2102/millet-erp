using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Aw.Application.IntegrationEvents;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Application.Commands.MarcarDropFallido;

public sealed class MarcarDropFallidoHandler : IRequestHandler<MarcarDropFallidoCommand>
{
    private readonly IntegracionesAwDbContext _db;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IClock _clock;

    public MarcarDropFallidoHandler(
        IntegracionesAwDbContext db,
        IIntegrationEventPublisher publisher,
        IClock clock)
    {
        _db = db;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task Handle(MarcarDropFallidoCommand request, CancellationToken cancellationToken)
    {
        var entidad = await _db.EntidadesExternas
            .FirstOrDefaultAsync(e => e.Id == request.EntidadExternaId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"EntidadExterna {request.EntidadExternaId} no encontrada para MarcarDropFallido.");

        if (request.IsTerminal)
        {
            entidad.MarcarDropFalladoTerminal(request.Error, request.ErrorKind);

            await _publisher.PublishAsync(
                new AwEdiEntregaFallida(
                    EmpresaId: entidad.EmpresaId,
                    OcurridoEn: _clock.UtcNow,
                    AggregateId: entidad.Id,
                    QuoteReference: entidad.ReferenciaExterna,
                    Error: request.Error,
                    ErrorKind: request.ErrorKind),
                cancellationToken);
        }
        else
        {
            entidad.IncrementarRetry(request.Error, request.ErrorKind);
            // NO emite event en reintentos transitorios — solo cuando
            // es terminal vale la pena alertar.
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
