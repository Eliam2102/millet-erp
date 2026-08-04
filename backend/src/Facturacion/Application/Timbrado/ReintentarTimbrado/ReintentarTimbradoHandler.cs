using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.NotasCredito;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using DominioCartaPorte = Millet.Facturacion.Domain.CartaPorte;

namespace Millet.Facturacion.Application.Timbrado.ReintentarTimbrado;

/// <summary>
/// Handler genérico del reintento de timbrado. Espeja el patrón de
/// <c>AplicarPedimentoHandler</c> (re-timbrado de un comprobante con folio
/// ya reservado) generalizado a los cinco tipos: carga polimórfica por la
/// raíz TPT, despacho por tipo para reconstruir la <c>CfdiEmision</c> con
/// los mismos builders de la emisión original, y réplica de los efectos
/// post-timbrado-exitoso de cada handler de emisión (eventos de
/// integración, asiento contable, pedido facturable + write-back A+W).
///
/// <para>
/// Una factura de venta fallida NUNCA tiene anticipos por amortizar: la
/// emisión con anticipos hace rollback total si el timbre no queda
/// <c>Timbrado</c> (<c>FACTURA_NO_TIMBRADA_PARA_AMORTIZAR</c>), así que un
/// <c>TimbradoFallido</c> persistido no llegó a amortizar nada.
/// </para>
/// </summary>
public sealed class ReintentarTimbradoHandler
    : IRequestHandler<ReintentarTimbradoCommand, ReintentarTimbradoResponse>
{
    /// <summary>
    /// Códigos con riesgo de timbre duplicado: el PAC pudo haber timbrado
    /// sin que llegara la respuesta (runbook 08 §5.bis, fila PAC_TIMEOUT).
    /// </summary>
    private static readonly string[] CodigosAmbiguos =
        ["PAC_TIMEOUT", "PAC_SIN_RESPUESTA", "PAC_RESPUESTA_INCOMPLETA"];

    private readonly FacturacionDbContext _db;
    private readonly ISender _sender;
    private readonly IPeriodoContablePort _periodo;
    private readonly ICfdiTimbradoPort _fiscal;
    private readonly ICfdiRepositorioPort _cfdiRepo;
    private readonly IIntegrationEventPublisher _eventos;
    private readonly IContabilidadAsientoPort _contabilidad;
    private readonly IClock _clock;

    public ReintentarTimbradoHandler(
        FacturacionDbContext db,
        ISender sender,
        IPeriodoContablePort periodo,
        ICfdiTimbradoPort fiscal,
        ICfdiRepositorioPort cfdiRepo,
        IIntegrationEventPublisher eventos,
        IContabilidadAsientoPort contabilidad,
        IClock clock)
    {
        _db = db;
        _sender = sender;
        _periodo = periodo;
        _fiscal = fiscal;
        _cfdiRepo = cfdiRepo;
        _eventos = eventos;
        _contabilidad = contabilidad;
        _clock = clock;
    }

    public async Task<ReintentarTimbradoResponse> Handle(
        ReintentarTimbradoCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;

        // Carga polimórfica por la raíz TPT: EF materializa el subtipo
        // concreto; las navegaciones del subtipo se cargan en el despacho.
        var comprobante = await _db.Comprobantes
            .FirstOrDefaultAsync(c => c.Id == command.ComprobanteId, ct)
            ?? throw new EntityNotFoundException(
                "COMPROBANTE_NO_ENCONTRADO", $"No existe el comprobante {command.ComprobanteId}.");

        if (comprobante.Estado != EstadoTimbrado.TimbradoFallido)
            throw new BusinessRuleException(
                "COMPROBANTE_NO_REINTENTABLE",
                $"Solo un comprobante en TimbradoFallido admite reintento de timbrado (actual: {comprobante.Estado}).");

        if (CodigosAmbiguos.Contains(comprobante.TimbradoErrorCodigo, StringComparer.OrdinalIgnoreCase)
            && !command.ConfirmarNoDuplicado)
        {
            throw new BusinessRuleException(
                "REINTENTO_REQUIERE_CONFIRMACION",
                $"El fallo '{comprobante.TimbradoErrorCodigo}' es AMBIGUO: el PAC pudo haber timbrado " +
                "sin que llegara la respuesta y reintentar duplicaría el CFDI ante el SAT. Verifica en el " +
                "dashboard de FiscalAPI que NO exista un timbre de este comprobante y confirma el reintento.");
        }

        var ahora = _clock.UtcNow;
        if (!await _periodo.EstaAbiertoAsync(ahora.Year, ahora.Month, ct))
            throw new BusinessRuleException(
                "PERIODO_CERRADO",
                $"El período contable {ahora.Year}-{ahora.Month:D2} está cerrado; no se puede timbrar.");

        comprobante.ReabrirParaReintentoTimbrado();

        var tipo = comprobante switch
        {
            FacturaVenta fv => await ReintentarFacturaVentaAsync(fv, ahora, ct),
            FacturaAnticipo fa => await ReintentarFacturaAnticipoAsync(fa, ahora, ct),
            NotaCredito nc => await ReintentarNotaCreditoAsync(nc, ahora, ct),
            ReciboPago repp => await ReintentarReciboPagoAsync(repp, ahora, ct),
            DominioCartaPorte.CartaPorte cp => await ReintentarCartaPorteAsync(cp, ahora, ct),
            _ => throw new BusinessRuleException(
                "COMPROBANTE_TIPO_NO_SOPORTADO",
                $"El tipo {comprobante.GetType().Name} no soporta reintento de timbrado."),
        };

        await _db.SaveChangesAsync(ct);

        return new ReintentarTimbradoResponse(
            Id: comprobante.Id,
            Tipo: tipo,
            Estado: comprobante.Estado.ToString(),
            Uuid: comprobante.Uuid,
            Folio: comprobante.Folio,
            TimbradoErrorCodigo: comprobante.TimbradoErrorCodigo,
            TimbradoErrorMensaje: comprobante.TimbradoErrorMensaje,
            Version: comprobante.Version);
    }

    // ───────────────────────── Despacho por tipo ─────────────────────────
    // Cada método carga las navegaciones del subtipo (la instancia ya está
    // trackeada — el identity map de EF puebla la misma entidad), re-timbra
    // y replica los efectos post-éxito de su handler de emisión.

    private async Task<string> ReintentarFacturaVentaAsync(
        FacturaVenta factura, DateTimeOffset ahora, CancellationToken ct)
    {
        await _db.FacturasVenta
            .Include(f => f.Lineas)
            .Include(f => f.Relaciones)
            .Include(f => f.ComplementoCce!)
                .ThenInclude(c => c.Lineas)
            .Where(f => f.Id == factura.Id)
            .LoadAsync(ct);

        await TimbradoEjecutor.TimbrarYAplicarAsync(_db, 
            factura, CfdiEmisionBuilder.DesdeFacturaVenta(factura, ahora),
            _fiscal, _cfdiRepo, ahora, ct);

        // Efectos de EmitirFacturaVentaHandler pasos 9 y B2/B3 (evento +
        // asiento + pedido + write-back A+W) que el intento fallido omitió.
        if (factura.Estado == EstadoTimbrado.Timbrado)
        {
            await _eventos.PublishAsync(new FacturaVentaTimbradaIntegrationEvent(
                factura.EmpresaId, ahora, factura.Id, factura.Uuid!, factura.Total, factura.Moneda, factura.PedidoFacturableId,
                factura.ReceptorRfc, factura.ReceptorNombre, factura.Folio, factura.MetodoPago, factura.FechaTimbrado),
                ct);
            await _contabilidad.RegistrarAsientoAsync(new AsientoContableSolicitud(
                factura.Id, "FacturaVenta", $"Factura {factura.Folio}", factura.Total, factura.Moneda,
                factura.PeriodoAnio, factura.PeriodoMes), ct);
        }

        if (factura.PedidoFacturableId is Guid pedidoId
            && factura.Estado != EstadoTimbrado.TimbradoFallido)
        {
            var pedido = await _db.PedidosFacturables
                .FirstOrDefaultAsync(p => p.Id == pedidoId, ct);

            // [Decisión 01-G] G5: desde F13 la emisión toma el pedido en el
            // primer intento, así que aquí normalmente ya está Facturado
            // apuntando a ESTA factura. El branch Importado cubre fallidas
            // previas a G5 (el pedido quedaba libre al fallar el timbre).
            if (pedido is { Estado: EstadoPedidoFacturable.Importado })
                pedido.MarcarFacturado(factura.Id);

            if (pedido?.ComprobanteVigenteId == factura.Id
                && pedido.Origen == OrigenPedido.Aw
                && factura.Estado == EstadoTimbrado.Timbrado)
            {
                var control = await _db.IngestaControles
                    .FirstOrDefaultAsync(c => c.PedidoFacturableId == pedido.Id, ct);
                if (control is not null)
                {
                    control.SolicitarWriteBackEstado("Facturado", factura.Uuid, _clock.UtcNow);
                    control.CambiarEstado(EstadoIngesta.Facturado);
                }
            }

            // RANURA-PR2: la emisión original no alcanzó a emitir la NC de la
            // ranura (el timbre falló antes) — se emite aquí al quedar
            // Timbrada. Idempotente si ya existe una NC de ranura vigente.
            await NcRanuraEmisor.EmitirSiAplicaAsync(
                _db, _sender, _fiscal, _cfdiRepo, _eventos,
                factura, pedido, usuarioEmisorId: null, ahora, ct);
        }

        return nameof(FacturaVenta);
    }

    private async Task<string> ReintentarFacturaAnticipoAsync(
        FacturaAnticipo factura, DateTimeOffset ahora, CancellationToken ct)
    {
        await _db.FacturasAnticipo
            .Include(f => f.Relaciones)
            .Where(f => f.Id == factura.Id)
            .LoadAsync(ct);

        await TimbradoEjecutor.TimbrarYAplicarAsync(_db, 
            factura, CfdiEmisionBuilder.DesdeFacturaAnticipo(factura, ahora),
            _fiscal, _cfdiRepo, ahora, ct);

        if (factura.Estado == EstadoTimbrado.Timbrado)
        {
            // El agregado Anticipo se creó en la emisión original aunque el
            // timbre fallara (EmitirFacturaAnticipoHandler persiste ambos).
            var anticipo = await _db.Anticipos
                .FirstOrDefaultAsync(a => a.FacturaAnticipoId == factura.Id, ct)
                ?? throw new EntityNotFoundException(
                    "ANTICIPO_NO_ENCONTRADO",
                    $"No existe el anticipo de la factura de anticipo {factura.Id}.");

            await _eventos.PublishAsync(new FacturaAnticipoTimbradaIntegrationEvent(
                factura.EmpresaId, ahora, factura.Id, anticipo.Id, factura.Uuid!, factura.Total, factura.Moneda),
                ct);
        }

        return nameof(FacturaAnticipo);
    }

    private async Task<string> ReintentarNotaCreditoAsync(
        NotaCredito nc, DateTimeOffset ahora, CancellationToken ct)
    {
        await _db.NotasCredito
            .Include(n => n.Relaciones)
            .Where(n => n.Id == nc.Id)
            .LoadAsync(ct);

        await TimbradoEjecutor.TimbrarYAplicarAsync(_db, 
            nc, CfdiEmisionBuilder.DesdeNotaCredito(nc, ahora),
            _fiscal, _cfdiRepo, ahora, ct);

        if (nc.Estado == EstadoTimbrado.Timbrado)
        {
            await _eventos.PublishAsync(new NotaCreditoTimbradaIntegrationEvent(
                nc.EmpresaId, ahora, nc.Id, nc.Motivo.ToString(), nc.Uuid!, nc.Total,
                nc.FacturaRelacionadaId, nc.AnticipoOrigenId), ct);
        }

        return nameof(NotaCredito);
    }

    private async Task<string> ReintentarReciboPagoAsync(
        ReciboPago repp, DateTimeOffset ahora, CancellationToken ct)
    {
        await _db.RecibosPago
            .Include(r => r.FacturasPagadas)
                .ThenInclude(f => f.Impuestos) // ImpuestosDR del complemento de pago (P9-H8)
            .Include(r => r.Relaciones)
            .Where(r => r.Id == repp.Id)
            .LoadAsync(ct);

        await TimbradoEjecutor.TimbrarYAplicarAsync(_db, 
            repp, CfdiEmisionBuilder.DesdeReciboPago(repp, ahora),
            _fiscal, _cfdiRepo, ahora, ct);

        if (repp.Estado == EstadoTimbrado.Timbrado)
        {
            await _eventos.PublishAsync(new ReciboPagoTimbradoIntegrationEvent(
                repp.EmpresaId, ahora, repp.Id, repp.Uuid!, repp.ImporteTotalPago,
                repp.FacturasPagadas.Sum(f => f.GananciaPerdidaCambiaria),
                repp.FacturasPagadas.Select(f => new ReppFacturaPagadaDetalle(
                    f.FacturaVentaId, f.ImportePagado, f.NumParcialidad, f.MonedaFactura, f.SaldoInsoluto)).ToList()), ct);
        }

        return nameof(ReciboPago);
    }

    private async Task<string> ReintentarCartaPorteAsync(
        DominioCartaPorte.CartaPorte cp, DateTimeOffset ahora, CancellationToken ct)
    {
        await _db.CartasPorte
            .Include(c => c.Mercancias)
            .Include(c => c.Relaciones)
            .Where(c => c.Id == cp.Id)
            .LoadAsync(ct);

        var vehiculo = await _db.Vehiculos.FirstOrDefaultAsync(v => v.Id == cp.VehiculoId, ct)
            ?? throw new EntityNotFoundException("VEHICULO_NO_ENCONTRADO", $"No existe el vehículo {cp.VehiculoId}.");
        var operador = await _db.Operadores.FirstOrDefaultAsync(o => o.Id == cp.OperadorId, ct)
            ?? throw new EntityNotFoundException("OPERADOR_NO_ENCONTRADO", $"No existe el operador {cp.OperadorId}.");

        await TimbradoEjecutor.TimbrarYAplicarAsync(_db, 
            cp, CfdiEmisionBuilder.DesdeCartaPorte(cp, vehiculo, operador, ahora),
            _fiscal, _cfdiRepo, ahora, ct);

        // EmitirCartaPorteHandler no publica eventos de integración.
        return "CartaPorte";
    }
}
