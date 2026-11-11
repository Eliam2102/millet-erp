using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;

/// <summary>
/// Registra la solicitud de cancelación pendiente de autorización (F6.5): valida
/// estado y cadena, pero no llama al PAC. El envío ocurre cuando el Contador
/// General la autoriza (<c>AutorizarCancelacionHandler</c>). Mientras tanto el
/// comprobante sigue Timbrado (vigente).
/// </summary>
public sealed class SolicitarCancelacionHandler(
    FacturacionDbContext db, ICurrentEmpresaContext empresa, ICurrentUserContext user, IClock clock)
    : IRequestHandler<SolicitarCancelacionCommand, SolicitarCancelacionResponse>
{
    public async Task<SolicitarCancelacionResponse> Handle(
        SolicitarCancelacionCommand command, CancellationToken cancellationToken)
    {
        if (empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");

        var comprobante = await CadenaCancelacion.CargarCancelableAsync(db, command.ComprobanteId, cancellationToken);

        var hayPendiente = await db.SolicitudesCancelacion.AnyAsync(
            s => s.ComprobanteId == comprobante.Id && s.Estado == EstadoSolicitudCancelacion.PendienteAutorizacion,
            cancellationToken);
        if (hayPendiente)
            throw new BusinessRuleException(
                "CANCELACION_YA_SOLICITADA", "El comprobante ya tiene una cancelación pendiente de autorización.");

        // M-235: las firmas se fijan al solicitar, según el tipo de operación y el importe en MXN.
        var firmas = await Firmas.FirmasCancelacion.RequeridasAsync(db, comprobante, cancellationToken);
        var solicitud = SolicitudCancelacion.Crear(
            empresaId, comprobante.Id, command.MotivoSat, command.UuidSustituto, clock.UtcNow, user.UserId, user.UserName,
            Firmas.FirmasCancelacion.ImporteMxn(comprobante), firmas);

        // PLATFORM-TODO(86e3txv0n): avisar al Contador General que hay una cancelación por autorizar cuando exista el aviso real.
        db.SolicitudesCancelacion.Add(solicitud);
        await db.SaveChangesAsync(cancellationToken);

        return SolicitarCancelacionResponse.De(comprobante, solicitud);
    }
}
