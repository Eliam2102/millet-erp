using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.Application.RfcsReceptores.ActualizarRfcReceptor;

/// <summary>
/// Toggle de los flags <c>DescargaHabilitada</c> / <c>RefreshHabilitada</c>
/// de un <see cref="RfcReceptor"/>. El RFC mismo no es editable —
/// para "cambiar" un RFC se elimina y se crea uno nuevo (preserva
/// auditoría del checkpoint).
/// </summary>
public sealed record ActualizarRfcReceptorCommand(
    Guid Id,
    bool DescargaHabilitada,
    bool RefreshHabilitada) : IRequest<RfcReceptorResponse>;

public sealed class ActualizarRfcReceptorHandler
    : IRequestHandler<ActualizarRfcReceptorCommand, RfcReceptorResponse>
{
    private readonly IntegracionesFiscalDbContext _db;
    private readonly IClock _clock;

    public ActualizarRfcReceptorHandler(IntegracionesFiscalDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<RfcReceptorResponse> Handle(
        ActualizarRfcReceptorCommand command, CancellationToken cancellationToken)
    {
        var entity = await _db.RfcsReceptores
            .FirstOrDefaultAsync(r => r.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "RFC_RECEPTOR_NO_ENCONTRADO",
                $"No existe el RFC receptor {command.Id}.");

        if (command.DescargaHabilitada) entity.HabilitarDescarga(); else entity.DeshabilitarDescarga();
        if (command.RefreshHabilitada) entity.HabilitarRefresh(); else entity.DeshabilitarRefresh();

        await _db.SaveChangesAsync(cancellationToken);
        return entity.ToResponse(_clock.UtcNow);
    }
}
