using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Cfdi.SolicitudesCancelacion;

/// <summary>Registra la solicitud que Fiscal recibió en el portal del SAT. El responsable es quien registra.</summary>
public sealed record RegistrarSolicitudCancelacionCommand(
    Guid CfdiId,
    AreaSolicitudCancelacion Area,
    DateTimeOffset? FechaRecepcion,
    string? Notas) : IRequest<SolicitudCancelacionResponse>;

public sealed class RegistrarSolicitudCancelacionValidator : AbstractValidator<RegistrarSolicitudCancelacionCommand>
{
    public RegistrarSolicitudCancelacionValidator()
    {
        RuleFor(c => c.CfdiId).NotEmpty();
        RuleFor(c => c.Area).IsInEnum();
        RuleFor(c => c.Notas).MaximumLength(1000);
    }
}

public sealed class RegistrarSolicitudCancelacionHandler
    : IRequestHandler<RegistrarSolicitudCancelacionCommand, SolicitudCancelacionResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public RegistrarSolicitudCancelacionHandler(CuentasPorPagarDbContext db, ICurrentUserContext currentUser, IClock clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public async Task<SolicitudCancelacionResponse> Handle(
        RegistrarSolicitudCancelacionCommand command, CancellationToken cancellationToken)
    {
        var cfdi = await _db.CfdisRecibidos.FirstOrDefaultAsync(c => c.Id == command.CfdiId, cancellationToken)
            ?? throw new EntityNotFoundException("CFDI_NO_ENCONTRADO", $"No existe un CFDI recibido con id '{command.CfdiId}'.");

        if (cfdi.EstatusSat == EstatusSatCfdi.Cancelado)
            throw new BusinessRuleException("CFDI_YA_CANCELADO_SAT", "El CFDI ya está cancelado ante el SAT.");
        if (await _db.SolicitudesCancelacionCfdi.AnyAsync(
                s => s.CfdiRecibidoId == cfdi.Id && s.Estado == EstadoSolicitudCancelacion.Pendiente, cancellationToken))
            throw new BusinessRuleException(
                "SOLICITUD_CANCELACION_PENDIENTE", "El CFDI ya tiene una solicitud de cancelación pendiente.");

        var solicitud = SolicitudCancelacionCfdi.Registrar(
            cfdi.EmpresaId, cfdi.Id, command.Area, command.FechaRecepcion ?? _clock.UtcNow, _currentUser.UserId, command.Notas);
        _db.SolicitudesCancelacionCfdi.Add(solicitud);
        await _db.SaveChangesAsync(cancellationToken);
        return SolicitudCancelacionResponse.De(solicitud);
    }
}
