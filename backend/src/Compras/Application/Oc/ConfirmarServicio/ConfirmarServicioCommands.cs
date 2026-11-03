using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Ports.Identidad;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.ConfirmarServicio;

public sealed record ConfirmarServicioOcCommand(Guid OrdenCompraId, string? Comentario) : IRequest;

public sealed record RevocarConfirmacionServicioOcCommand(Guid OrdenCompraId, string Motivo) : IRequest;

public sealed record MarcarServicioNoConformeOcCommand(Guid OrdenCompraId, string Motivo) : IRequest;

public sealed class ConfirmarServicioOcValidator : AbstractValidator<ConfirmarServicioOcCommand>
{
    public ConfirmarServicioOcValidator() => RuleFor(c => c.Comentario).MaximumLength(500);
}

public sealed class RevocarConfirmacionServicioOcValidator : AbstractValidator<RevocarConfirmacionServicioOcCommand>
{
    public RevocarConfirmacionServicioOcValidator() =>
        RuleFor(c => c.Motivo).NotEmpty().WithMessage("Indica el motivo para deshacer la confirmación.").MaximumLength(500);
}

public sealed class MarcarServicioNoConformeOcValidator : AbstractValidator<MarcarServicioNoConformeOcCommand>
{
    public MarcarServicioNoConformeOcValidator() =>
        RuleFor(c => c.Motivo).NotEmpty().WithMessage("Indica por qué no quedaste conforme.").MaximumLength(500);
}

/// <summary>
/// 86e3tacbn: conformidad del servicio por OC. El permiso lo exige el endpoint; el dominio exige que el usuario
/// sea del departamento solicitante de una línea de servicio.
/// </summary>
public sealed class ConfirmarServicioOcHandler(ComprasDbContext db, ICurrentUserContext currentUser, IClock clock,
    IUsuarioReadPort usuarios, IPublisher publisher, ILogger<ConfirmarServicioOcHandler> logger) :
    IRequestHandler<ConfirmarServicioOcCommand>,
    IRequestHandler<RevocarConfirmacionServicioOcCommand>,
    IRequestHandler<MarcarServicioNoConformeOcCommand>
{
    public Task Handle(ConfirmarServicioOcCommand r, CancellationToken cancellationToken) =>
        Ejecutar(r.OrdenCompraId, (oc, u, d, ahora) => oc.ConfirmarServicio(u, d, r.Comentario, ahora), cancellationToken);

    public Task Handle(RevocarConfirmacionServicioOcCommand r, CancellationToken cancellationToken) =>
        Ejecutar(r.OrdenCompraId, (oc, u, d, ahora) => oc.RevocarConfirmacionServicio(u, d, r.Motivo, ahora), cancellationToken);

    public async Task Handle(MarcarServicioNoConformeOcCommand r, CancellationToken cancellationToken)
    {
        await Ejecutar(r.OrdenCompraId, (oc, u, d, ahora) =>
        {
            oc.MarcarServicioNoConforme(u, d, r.Motivo, ahora);
            return null;
        }, cancellationToken);
        // PLATFORM-TODO(<Notificaciones>): enviar el aviso a Compras por correo/SignalR cuando exista el módulo de
        // notificaciones (ADR-0026). Hoy queda registrado en la OC (banner para Compras) y en bitácora.
        logger.LogWarning("[ConfirmarServicioOc] El área solicitante no quedó conforme con el servicio de la OC {OcId}: {Motivo}",
            r.OrdenCompraId, r.Motivo);
    }

    private async Task Ejecutar(Guid ocId,
        Func<Domain.Oc.OrdenCompra, Guid, Guid?, DateTimeOffset, Domain.Oc.Events.OrdenCompraServicioConfirmadoEvent?> accion,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        var oc = await db.OrdenesCompra.Include(o => o.Lineas).FirstOrDefaultAsync(o => o.Id == ocId, cancellationToken)
            ?? throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA", "No se encontró la orden de compra.");
        var evento = accion(oc, userId, await usuarios.ObtenerDepartamentoAsync(userId, cancellationToken), clock.UtcNow);
        if (evento is not null) await publisher.Publish(evento, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
