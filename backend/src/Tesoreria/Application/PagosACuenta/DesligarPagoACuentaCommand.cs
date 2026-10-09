using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.PagosACuenta;

public sealed record DesligarPagoACuentaCommand(Guid MovimientoId, Guid AplicacionId, string Motivo) : IRequest;
public sealed class DesligarPagoACuentaValidator : AbstractValidator<DesligarPagoACuentaCommand>
{
    public DesligarPagoACuentaValidator()
    {
        RuleFor(x => x.MovimientoId).NotEmpty();
        RuleFor(x => x.AplicacionId).NotEmpty();
        RuleFor(x => x.Motivo).NotEmpty().MaximumLength(400);
    }
}
public sealed class DesligarPagoACuentaHandler(TesoreriaDbContext db, IIntegrationEventPublisher publisher, IClock clock)
    : IRequestHandler<DesligarPagoACuentaCommand>
{
    public async Task Handle(DesligarPagoACuentaCommand command, CancellationToken cancellationToken)
    {
        var movimiento = await db.MovimientosBancarios.FirstOrDefaultAsync(m => m.Id == command.MovimientoId, cancellationToken)
            ?? throw new EntityNotFoundException("MOV_NO_ENCONTRADO", "No se encontró el pago a cuenta.");
        if (movimiento.MotivoNoAplicado is null || movimiento.Sentido != SentidoMovimiento.Egreso || movimiento.ContramovimientoDe is not null)
            throw new BusinessRuleException("DESLIGA_NO_ES_PAGO_A_CUENTA", "Solo se puede desligar un pago a cuenta.");
        // Un pago totalmente aplicado podría reabrirse cuando ya existe otro: conserva R5.
        if (await db.MovimientosBancarios.AnyAsync(m => m.Id != movimiento.Id && m.BeneficiarioRef == movimiento.BeneficiarioRef &&
                m.MotivoNoAplicado != null && m.ContramovimientoDe == null &&
                (m.EstadoAplicacion == EstadoAplicacionMovimiento.NoAplicado || m.EstadoAplicacion == EstadoAplicacionMovimiento.AplicadoParcial), cancellationToken))
            throw new BusinessRuleException("PAGO_CUENTA_ABIERTO_EXISTENTE", "El proveedor ya tiene otro pago a cuenta abierto. Completa su aplicación antes de reabrir este pago.");
        var aplicaciones = await db.AplicacionesPagoProveedor.Where(a => a.MovimientoId == movimiento.Id).ToListAsync(cancellationToken);
        var aplicacion = aplicaciones.FirstOrDefault(a => a.Id == command.AplicacionId)
            ?? throw new EntityNotFoundException("PAGO_NO_ENCONTRADO", "No se encontró la aplicación en este pago a cuenta.");
        var pasivo = await db.PasivosPendientesPago.FirstOrDefaultAsync(p => p.FacturaProveedorId == aplicacion.FacturaProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException("PAGO_PASIVO_NO_ENCONTRADO", "No se encontró el pasivo de la aplicación.");
        aplicacion.Revertir(command.Motivo);
        pasivo.RevertirPago(aplicacion.ImporteAplicado);
        movimiento.ActualizarEstadoAplicacion(aplicaciones.Where(a => !a.Revertida).Sum(a => a.ImporteAplicado));
        await publisher.PublishAsync(new PagoFacturaProveedorRevertidoIntegrationEvent(movimiento.EmpresaId,
            clock.UtcNow, aplicacion.FacturaProveedorId, aplicacion.Id, aplicacion.ImporteAplicado,
            movimiento.Moneda, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), command.Motivo.Trim()), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
