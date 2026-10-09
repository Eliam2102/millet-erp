using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Eventos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.EventListeners;

public sealed record PagoClienteConfirmadoPayload(Guid EmpresaId, DateTimeOffset OcurridoEn,
    Guid? PropuestaId, Guid ClienteId, Guid MovimientoBancarioId, Guid? ConfirmadaPor);
public sealed record PropuestaRechazadaPayload(Guid EmpresaId, DateTimeOffset OcurridoEn,
    Guid PropuestaId, Guid? ClienteId, string Motivo, Guid RechazadaPor);
public sealed record ResolverPropuestaTesoreriaCommand(Guid EventId, Guid EmpresaId,
    Guid PropuestaId, Guid UsuarioId, DateTimeOffset OcurridoEn,
    Guid? MovimientoBancarioId, string? MotivoRechazo) : IRequest;

public sealed class ResolverPropuestaTesoreriaHandler(CuentasPorCobrarDbContext db, IClock clock)
    : IRequestHandler<ResolverPropuestaTesoreriaCommand>
{
    public const string ConfirmadaEventType = "tesoreria.pago-cliente.confirmado.v1";
    public const string RechazadaEventType = "tesoreria.propuesta-aplicacion.rechazada.v1";

    public async Task Handle(ResolverPropuestaTesoreriaCommand command, CancellationToken cancellationToken)
    {
        var tipo = command.MovimientoBancarioId is null ? RechazadaEventType : ConfirmadaEventType;
        if (await db.EventosProcesados.AnyAsync(e => e.EventoId == command.EventId && e.EventoTipo == tipo, cancellationToken)) return;
        var propuesta = await db.PropuestasAplicacionPago.FirstOrDefaultAsync(
            p => p.Id == command.PropuestaId && p.EmpresaId == command.EmpresaId, cancellationToken)
            ?? throw new EntityNotFoundException("PAP_NO_ENCONTRADA", "La propuesta de CxC todavía no está disponible; se reintentará.");
        if (command.UsuarioId == Guid.Empty)
            throw new BusinessRuleException("PAP_USUARIO_NO_IDENTIFICADO", "El evento no identifica a quien resolvió la propuesta.");
        if (propuesta.Estado == EstadoPropuestaAplicacion.Propuesta)
        {
            if (command.MovimientoBancarioId is Guid movimientoId)
                propuesta.Confirmar(command.UsuarioId, command.OcurridoEn, movimientoId);
            else
                propuesta.Rechazar(command.UsuarioId, command.MotivoRechazo ?? "", command.OcurridoEn);
        }
        else if ((command.MovimientoBancarioId is not null &&
                     (propuesta.Estado != EstadoPropuestaAplicacion.Confirmada || propuesta.MovimientoBancarioId != command.MovimientoBancarioId)) ||
                 (command.MovimientoBancarioId is null && propuesta.Estado != EstadoPropuestaAplicacion.Rechazada))
            throw new BusinessRuleException("PAP_RESOLUCION_INCONSISTENTE", "La propuesta ya tiene una resolución distinta.");
        db.EventosProcesados.Add(new EventoProcesado(command.EventId, tipo, clock.UtcNow, $"Propuesta={propuesta.Id}"));
        await db.SaveChangesAsync(cancellationToken);
    }
}
