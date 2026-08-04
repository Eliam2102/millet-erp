using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.EnviarARevision;

public sealed record EnviarFacturaARevisionCommand(
    Guid Id,
    int VersionEsperada,
    Guid MotivoRevisionId,
    Guid DependenciaRevisoraId) : IRequest<EnviarFacturaARevisionResponse>;

public sealed record EnviarFacturaARevisionResponse(
    Guid Id,
    EstadoPasivo Estado,
    bool EnRevision,
    Guid? MotivoRevisionId,
    Guid? DependenciaRevisoraId,
    DateTimeOffset? FechaEntradaRevision,
    int Version);

public sealed class EnviarFacturaARevisionValidator : AbstractValidator<EnviarFacturaARevisionCommand>
{
    public EnviarFacturaARevisionValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
        RuleFor(c => c.MotivoRevisionId).NotEmpty();
        RuleFor(c => c.DependenciaRevisoraId).NotEmpty();
    }
}

public sealed class EnviarFacturaARevisionHandler : IRequestHandler<EnviarFacturaARevisionCommand, EnviarFacturaARevisionResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IDependenciaRevisoraReadPort _dependenciaPort;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public EnviarFacturaARevisionHandler(
        CuentasPorPagarDbContext db,
        IDependenciaRevisoraReadPort dependenciaPort,
        ICurrentUserContext currentUser,
        IClock clock)
    {
        _db = db; _dependenciaPort = dependenciaPort; _currentUser = currentUser; _clock = clock;
    }

    public async Task<EnviarFacturaARevisionResponse> Handle(EnviarFacturaARevisionCommand command, CancellationToken cancellationToken)
    {
        var factura = await _db.FacturasProveedor
            .FirstOrDefaultAsync(f => f.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "FACTURA_NO_ENCONTRADA",
                $"No se encontró la factura con id '{command.Id}'.");

        if (factura.Version != command.VersionEsperada)
        {
            throw new ConcurrencyException(nameof(FacturaProveedor), factura.Id);
        }

        var motivo = await _db.MotivosRevision
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == command.MotivoRevisionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "MOTIVO_REVISION_NO_ENCONTRADO",
                $"No existe el motivo de revisión '{command.MotivoRevisionId}'.");

        if (!motivo.Activo)
        {
            throw new BusinessRuleException(
                "MOTIVO_REVISION_INACTIVO",
                $"El motivo de revisión '{motivo.Codigo}' está inactivo y no puede usarse.");
        }

        var dependencia = await _dependenciaPort.ObtenerAsync(command.DependenciaRevisoraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "DEPENDENCIA_REVISORA_NO_ENCONTRADA",
                $"No existe la dependencia revisora '{command.DependenciaRevisoraId}'.");

        if (!dependencia.Activa)
        {
            throw new BusinessRuleException(
                "DEPENDENCIA_REVISORA_INACTIVA",
                $"La dependencia revisora '{dependencia.Codigo}' está inactiva.");
        }

        factura.EnviarARevision(motivo.Id, dependencia.Id, _currentUser.UserId, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        return new EnviarFacturaARevisionResponse(
            Id: factura.Id,
            Estado: factura.Estado,
            EnRevision: factura.EnRevision,
            MotivoRevisionId: factura.MotivoRevisionId,
            DependenciaRevisoraId: factura.DependenciaRevisoraId,
            FechaEntradaRevision: factura.FechaEntradaRevision,
            Version: factura.Version);
    }
}
