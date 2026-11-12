using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Idempotencia;
using Millet.Compras.Domain.Ports.Cxp;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Application.Oc.Eventos.Cxp;

/// <summary>
/// Al cancelar una factura, CxP manda el facturado vigente por línea de OC (sin la cancelada). Se aplica por el mismo
/// camino que <c>factura.registrada</c> (<see cref="FacturaProveedorRegistradaEvent"/> fija el acumulado), así la OC baja
/// lo facturado y se reabre si estaba cerrada.
/// </summary>
public sealed record FacturaProveedorCanceladaCommand(
    Guid EventoId,
    FacturaProveedorCanceladaPayload Payload) : IRequest;

public sealed class FacturaProveedorCanceladaCommandHandler(
    ComprasDbContext db, IPublisher publisher, ILogger<FacturaProveedorCanceladaCommandHandler> logger,
    Millet.Compras.Application.PublicPorts.IFacturaServicioCeReadPort? facturasCe = null)
    : IRequestHandler<FacturaProveedorCanceladaCommand>
{
    public const string EventType = "cuentas_por_pagar.factura.cancelada.v1";

    public async Task Handle(FacturaProveedorCanceladaCommand command, CancellationToken cancellationToken)
    {
        if (await db.EventosProcesados.AsNoTracking().AnyAsync(e => e.EventoId == command.EventoId && e.EventoTipo == EventType, cancellationToken))
            return;

        var p = command.Payload;
        if (p.OrdenCompraId is Guid ocId && p.LineasAcumuladasOc is { Count: > 0 } lineas)
        {
            foreach (var linea in lineas)
                await publisher.Publish(new FacturaProveedorRegistradaEvent(
                    OrdenCompraId: ocId,
                    LineaOrdenCompraId: linea.LineaOcId,
                    EmpresaId: p.EmpresaId,
                    CantidadFacturadaAcumulada: linea.CantidadAcumulada,
                    OcurridoEn: p.OcurridoEn), cancellationToken);
        }
        else
        {
            logger.LogInformation("Factura {FacturaId} cancelada sin acumulado por línea de OC — sin efecto en Compras.", p.FacturaProveedorId);
        }

        if (p.OrdenCompraId is Guid servicioId)
        {
            var servicio = await db.OrdenesCompra.Include(o => o.Autorizaciones)
                .SingleOrDefaultAsync(o => o.Id == servicioId && o.EmpresaId == p.EmpresaId, cancellationToken);
            if (servicio is { EsServicioComercioExterior: true })
            {
                if (facturasCe is null) throw new Millet.SharedKernel.Application.Exceptions.BusinessRuleException("CE_VALIDACION_NO_DISPONIBLE", "No se pudo comprobar la factura vigente del servicio.");
                // Un aviso antiguo no invalida la revisión de una factura sustituta que sigue vigente.
                if (!await facturasCe.TienePasivoVigenteAsync(p.EmpresaId, servicioId, cancellationToken))
                    servicio.DevolverServicioPorFacturaCancelada(p.FacturaProveedorId);
            }
        }

        db.EventosProcesados.Add(new EventoProcesado(command.EventoId, EventType, $"FacturaId={p.FacturaProveedorId}"));
        await db.SaveChangesAsync(cancellationToken);
    }
}
