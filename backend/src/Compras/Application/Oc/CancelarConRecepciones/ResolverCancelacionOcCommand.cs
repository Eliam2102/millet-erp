using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Integration.AlmCompra;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.CancelarConRecepciones;

public sealed record ResolverCancelacionOcCommand(Guid OrdenCompraId, bool Confirmar, string Motivo) : IRequest;

public sealed class ResolverCancelacionOcValidator : AbstractValidator<ResolverCancelacionOcCommand>
{
    public ResolverCancelacionOcValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().WithMessage("Escribe el motivo de tu decisión.").MaximumLength(500);
    }
}

public sealed class ResolverCancelacionOcHandler(ComprasDbContext db, ICurrentUserContext currentUser,
    IClock clock, IPublisher publisher, AlmCompraEncolador? almCompra = null) : IRequestHandler<ResolverCancelacionOcCommand>
{
    public async Task Handle(ResolverCancelacionOcCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        var oc = await db.OrdenesCompra.Include(o => o.Lineas).Include(o => o.SolicitudesCancelacion)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA", "No se encontró la orden de compra.");
        if (command.Confirmar)
        {
            // 86e3jvu83: si la compra ya está en Almacenes, la segunda firma no cancela todavía: espera el acuse de COMPRA_CANCELADA.
            if (almCompra is not null && await almCompra.EnviadaAAlmacenesAsync(oc.Id, cancellationToken))
            {
                oc.ConfirmarCancelacionConRecepcionesEsperaAlmacenes(userId, clock.UtcNow, command.Motivo);
                await almCompra.EncolarCancelacionAsync(oc.Id,
                    oc.SolicitudesCancelacion.OrderBy(x => x.FechaSolicitud).Last().MotivoSolicitud, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            var resultado = oc.ConfirmarCancelacionConRecepciones(userId, clock.UtcNow, command.Motivo);
            var rqIds = resultado.LiberacionesParciales.Select(l => l.RequisicionId).Distinct().ToArray();
            var rqs = await db.Requisiciones.Where(r => rqIds.Contains(r.Id)).ToListAsync(cancellationToken);
            foreach (var rq in rqs.Where(r => r.ComprometidaEnOcId == oc.Id)) rq.LiberarDeOc();
            await Cotizaciones.CotizacionesComparativo.LiberarPorOcAsync(db, oc.Id, userId, clock.UtcNow,
                $"OC cancelada con recepciones: {command.Motivo}", cancellationToken);
            // El saldo se deriva de las líneas; lo recibido permanece consumido en la OC cancelada.
            await publisher.Publish(resultado.EventoCancelada, cancellationToken);
        }
        else
        {
            oc.RechazarCancelacion(userId, clock.UtcNow, command.Motivo);
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
