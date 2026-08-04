using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;

/// <summary>
/// Orquesta la solicitud de cancelación: valida estado y cadena, llama al PAC
/// (stub), registra la <see cref="SolicitudCancelacion"/> y aplica la FSM del
/// comprobante. Si el PAC acepta de inmediato, cancela y revierte efectos; si
/// queda en proceso, el <c>CancelacionSatPollerWorker</c> lo resuelve; si lo
/// rechaza, el comprobante vuelve a Timbrado.
/// </summary>
public sealed class SolicitarCancelacionHandler
    : IRequestHandler<SolicitarCancelacionCommand, SolicitarCancelacionResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICfdiTimbradoPort _fiscal;
    private readonly IIntegrationEventPublisher _eventos;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly IClock _clock;

    public SolicitarCancelacionHandler(
        FacturacionDbContext db, ICfdiTimbradoPort fiscal, IIntegrationEventPublisher eventos, ICurrentEmpresaContext empresa, IClock clock)
    {
        _db = db;
        _fiscal = fiscal;
        _eventos = eventos;
        _empresa = empresa;
        _clock = clock;
    }

    public async Task<SolicitarCancelacionResponse> Handle(
        SolicitarCancelacionCommand command, CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");

        var comprobante = await _db.Comprobantes
            .FirstOrDefaultAsync(c => c.Id == command.ComprobanteId, cancellationToken)
            ?? throw new EntityNotFoundException("COMPROBANTE_NO_ENCONTRADO", $"No existe el comprobante {command.ComprobanteId}.");

        if (comprobante.Estado != EstadoTimbrado.Timbrado)
            throw new BusinessRuleException(
                "COMPROBANTE_NO_CANCELABLE",
                $"Solo un comprobante Timbrado puede cancelarse (estado actual: {comprobante.Estado}).");

        // Validación de cadena: un anticipo con NCs de amortización vigentes no se
        // cancela hasta cancelar esas NCs (invariante 8).
        await ValidarCadenaAsync(comprobante, cancellationToken);

        var ahora = _clock.UtcNow;
        var resultado = await _fiscal.CancelarAsync(
            new CancelacionSolicitud(comprobante.EmpresaId, comprobante.Uuid ?? string.Empty, comprobante.RfcEmisor, command.MotivoSat, command.UuidSustituto),
            cancellationToken);

        var solicitud = SolicitudCancelacion.Crear(empresaId, comprobante.Id, command.MotivoSat, command.UuidSustituto, ahora);
        comprobante.MarcarCancelacionPendiente();

        switch (resultado.Estado)
        {
            case CancelacionEstado.Aceptada:
                comprobante.MarcarCancelado();
                solicitud.MarcarAceptada(resultado.EstatusSat, ahora);
                await AplicarEfectosCancelacionAsync(comprobante, cancellationToken);
                // F10-PR1: evento de comprobante cancelado (reversa fiscal/financiera).
                await _eventos.PublishAsync(new ComprobanteCanceladoIntegrationEvent(
                    comprobante.EmpresaId, ahora, comprobante.Id, comprobante.Tipo.ToString(), comprobante.Uuid ?? string.Empty),
                    cancellationToken);
                break;

            case CancelacionEstado.Solicitada:
            case CancelacionEstado.EnProceso:
                solicitud.MarcarEnProceso(resultado.EstatusSat);
                break;

            case CancelacionEstado.Rechazada:
            case CancelacionEstado.Error:
                comprobante.RevertirCancelacion();
                solicitud.MarcarRechazada(resultado.ErrorMensaje ?? resultado.EstatusSat, ahora);
                break;
        }

        _db.SolicitudesCancelacion.Add(solicitud);
        await _db.SaveChangesAsync(cancellationToken);

        return new SolicitarCancelacionResponse(
            SolicitudId: solicitud.Id,
            ComprobanteId: comprobante.Id,
            EstadoComprobante: comprobante.Estado.ToString(),
            EstadoSolicitud: solicitud.Estado.ToString(),
            EstatusSat: solicitud.EstatusSat);
    }

    /// <summary>
    /// Si el comprobante es una factura de anticipo, bloquea la cancelación cuando
    /// el anticipo tiene amortizaciones cuyas NCs siguen vigentes (invariante 8).
    /// </summary>
    private async Task ValidarCadenaAsync(Comprobante comprobante, CancellationToken cancellationToken)
    {
        if (comprobante is not FacturaAnticipo) return;

        var anticipo = await _db.Anticipos
            .Include(a => a.Vinculaciones)
            .FirstOrDefaultAsync(a => a.FacturaAnticipoId == comprobante.Id, cancellationToken);
        if (anticipo is null) return;

        var ncIds = anticipo.Vinculaciones
            .Where(v => v.NcAmortizacionId is not null)
            .Select(v => v.NcAmortizacionId!.Value)
            .ToList();
        if (ncIds.Count == 0) return;

        var hayNcsVigentes = await _db.NotasCredito
            .AnyAsync(n => ncIds.Contains(n.Id) && n.Estado != EstadoTimbrado.Cancelado, cancellationToken);
        if (hayNcsVigentes)
            throw new BusinessRuleException(
                "ANTICIPO_AMORTIZADO_NCS_VIGENTES",
                "No se puede cancelar el anticipo: primero cancela las NC de amortización relacionadas (invariante 8).");
    }

    /// <summary>
    /// Efectos de una cancelación aceptada: la factura final libera su pedido
    /// (vuelve a Importado, re-facturable, invariante 5); el anticipo cancelado
    /// pasa a Cancelado.
    /// </summary>
    private async Task AplicarEfectosCancelacionAsync(Comprobante comprobante, CancellationToken cancellationToken)
    {
        switch (comprobante)
        {
            case FacturaVenta { PedidoFacturableId: { } pedidoId }:
                var pedido = await _db.PedidosFacturables.FirstOrDefaultAsync(p => p.Id == pedidoId, cancellationToken);
                pedido?.RevertirAFacturable();

                // ADR-0048 D3 (PR5): un pedido A+W re-facturable avisa de
                // vuelta a la tabla-puente (estado SinFacturar; el uuid del
                // CFDI cancelado se conserva en la fila — COALESCE del adapter).
                if (pedido?.Origen == OrigenPedido.Aw)
                {
                    var control = await _db.IngestaControles
                        .FirstOrDefaultAsync(c => c.PedidoFacturableId == pedido.Id, cancellationToken);
                    if (control is not null)
                    {
                        control.SolicitarWriteBackEstado("SinFacturar", uuid: null, _clock.UtcNow);
                        control.CambiarEstado(EstadoIngesta.Importado);
                    }
                }
                break;

            case FacturaAnticipo:
                var anticipo = await _db.Anticipos.FirstOrDefaultAsync(a => a.FacturaAnticipoId == comprobante.Id, cancellationToken);
                anticipo?.Cancelar();
                break;
        }
    }
}
