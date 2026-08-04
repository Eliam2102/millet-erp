using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.CancelarFactura;

public sealed record CancelarFacturaCommand(
    Guid Id,
    int VersionEsperada,
    MotivoCancelacion Motivo,
    string? Texto) : IRequest<CancelarFacturaResponse>;

public sealed record CancelarFacturaResponse(Guid Id, EstadoPasivo Estado, int Version);

public sealed class CancelarFacturaValidator : AbstractValidator<CancelarFacturaCommand>
{
    public CancelarFacturaValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Texto).MaximumLength(400);
    }
}

public sealed class CancelarFacturaHandler : IRequestHandler<CancelarFacturaCommand, CancelarFacturaResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public CancelarFacturaHandler(
        CuentasPorPagarDbContext db,
        ICurrentUserContext currentUser,
        IMediator mediator,
        IClock clock)
    {
        _db = db; _currentUser = currentUser; _mediator = mediator; _clock = clock;
    }

    public async Task<CancelarFacturaResponse> Handle(CancelarFacturaCommand command, CancellationToken cancellationToken)
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

        var ahora = _clock.UtcNow;
        factura.Cancelar(command.Motivo, command.Texto, _currentUser.UserId, ahora);

        // Publica el evento ANTES del SaveChanges para que entre en la
        // misma TX del outbox interceptor. Si el motivo es
        // RechazadaPorTolerancia (uso atípico desde este endpoint —
        // generalmente sale del handler de captura), emite el evento
        // específico; el resto usa el genérico Cancelada.
        if (command.Motivo == MotivoCancelacion.RechazadaPorTolerancia && factura.OrdenCompraId is Guid ocId)
        {
            await _mediator.Publish(new FacturaProveedorRechazadaPorToleranciaDomainEvent(
                EmpresaId: factura.EmpresaId,
                FacturaProveedorId: factura.Id,
                OrdenCompraId: ocId,
                TotalFactura: factura.Total,
                TotalOc: factura.Total - factura.DiferenciaContraOc,
                Diferencia: factura.DiferenciaContraOc,
                ToleranciaAplicada: $"{factura.ToleranciaTipo}:{factura.ToleranciaValor}",
                OcurridoEn: ahora), cancellationToken);
        }
        else
        {
            await _mediator.Publish(new FacturaProveedorCanceladaDomainEvent(
                EmpresaId: factura.EmpresaId,
                FacturaProveedorId: factura.Id,
                OrdenCompraId: factura.OrdenCompraId,
                Motivo: command.Motivo,
                MotivoTexto: command.Texto,
                OcurridoEn: ahora), cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new CancelarFacturaResponse(factura.Id, factura.Estado, factura.Version);
    }
}
