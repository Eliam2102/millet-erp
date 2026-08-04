using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.NotasCredito;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Facturas.AplicarPedimento;

/// <summary>
/// Completa una factura retenida en <c>PendientePedimento</c>: aplica el
/// pedimento (vuelve a Borrador) y la timbra (stub hasta F12). El folio ya se
/// reservó en la emisión inicial, así que aquí solo se aplica el pedimento y se
/// timbra.
/// </summary>
public sealed class AplicarPedimentoHandler : IRequestHandler<AplicarPedimentoCommand, AplicarPedimentoResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ISender _sender;
    private readonly IPeriodoContablePort _periodo;
    private readonly ICfdiTimbradoPort _fiscal;
    private readonly ICfdiRepositorioPort _cfdiRepo;
    private readonly IIntegrationEventPublisher _eventos;
    private readonly IClock _clock;

    public AplicarPedimentoHandler(
        FacturacionDbContext db, ISender sender, IPeriodoContablePort periodo, ICfdiTimbradoPort fiscal,
        ICfdiRepositorioPort cfdiRepo, IIntegrationEventPublisher eventos, IClock clock)
    {
        _db = db;
        _sender = sender;
        _periodo = periodo;
        _fiscal = fiscal;
        _cfdiRepo = cfdiRepo;
        _eventos = eventos;
        _clock = clock;
    }

    public async Task<AplicarPedimentoResponse> Handle(AplicarPedimentoCommand command, CancellationToken cancellationToken)
    {
        var factura = await _db.FacturasVenta
            .Include(f => f.Lineas)
            .FirstOrDefaultAsync(f => f.Id == command.FacturaVentaId, cancellationToken)
            ?? throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", $"No existe la factura {command.FacturaVentaId}.");

        if (factura.Estado != EstadoTimbrado.PendientePedimento)
            throw new BusinessRuleException(
                "FACTURA_NO_PENDIENTE_PEDIMENTO",
                $"Solo una factura en PendientePedimento admite pedimento (estado actual: {factura.Estado}).");

        var ahora = _clock.UtcNow;
        if (!await _periodo.EstaAbiertoAsync(ahora.Year, ahora.Month, cancellationToken))
            throw new BusinessRuleException("PERIODO_CERRADO", $"El período contable {ahora.Year}-{ahora.Month:D2} está cerrado; no se puede timbrar.");

        // Aplica el pedimento (vuelve a Borrador) y timbra.
        factura.AplicarPedimento(command.Pedimento, command.FechaDocAduanero, command.IdentificacionMercancia);

        await TimbradoEjecutor.TimbrarYAplicarAsync(_db, 
            factura, CfdiEmisionBuilder.DesdeFacturaVenta(factura, ahora),
            _fiscal, _cfdiRepo, ahora, cancellationToken);

        // F10-PR1: evento de factura timbrada (tras aplicar el pedimento).
        if (factura.Estado == EstadoTimbrado.Timbrado)
            await _eventos.PublishAsync(new FacturaVentaTimbradaIntegrationEvent(
                factura.EmpresaId, ahora, factura.Id, factura.Uuid!, factura.Total, factura.Moneda, factura.PedidoFacturableId,
                factura.ReceptorRfc, factura.ReceptorNombre, factura.Folio, factura.MetodoPago, factura.FechaTimbrado),
                cancellationToken);

        // RANURA-PR2: la factura retenida por pedimento no alcanzó a emitir
        // la NC de la ranura en la emisión — se emite aquí al quedar Timbrada.
        await NcRanuraEmisor.EmitirSiAplicaAsync(
            _db, _sender, _fiscal, _cfdiRepo, _eventos,
            factura, pedido: null, usuarioEmisorId: null, ahora, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new AplicarPedimentoResponse(factura.Id, factura.Estado.ToString(), factura.Uuid, factura.Folio);
    }

}
