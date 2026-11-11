using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.NotasCredito.SolicitarReembolso;
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

    private readonly ValidadorReceptorFiscal _receptorFiscal;
    private readonly FacturacionDbContext _db;
    private readonly ISender _sender;
    private readonly IPeriodoContablePort _periodo;
    private readonly ICfdiTimbradoPort _fiscal;
    private readonly ICfdiRepositorioPort _cfdiRepo;
    private readonly IIntegrationEventPublisher _eventos;
    private readonly IClock _clock;
    private readonly IProductosReadPort? _productos;
    private readonly IEmpresaFiscalReadPort? _empresaFiscal;
    private readonly EfectosPostTimbrado _efectos;
    private readonly ICatalogosSatReadPort? _catalogos;

    public ReintentarTimbradoHandler(
        FacturacionDbContext db,
        ISender sender,
        IPeriodoContablePort periodo,
        ICfdiTimbradoPort fiscal,
        ICfdiRepositorioPort cfdiRepo,
        IIntegrationEventPublisher eventos,
        IClock clock, ValidadorReceptorFiscal receptorFiscal,
        // U1.6: tipo A+W de las líneas en el evento contable; opcional para no
        // romper composiciones existentes (sin puerto, TipoProducto = null).
        IProductosReadPort? productos = null, IEmpresaFiscalReadPort? empresaFiscal = null,
        // CCE121: TipoCambioUSD del catálogo para la fecha del reintento; sin puerto se conserva el guardado.
        ICatalogosSatReadPort? catalogos = null)
    {
        _catalogos = catalogos;
        _receptorFiscal = receptorFiscal;
        _db = db;
        _sender = sender;
        _periodo = periodo;
        _fiscal = fiscal;
        _cfdiRepo = cfdiRepo;
        _eventos = eventos;
        _clock = clock;
        _productos = productos;
        _empresaFiscal = empresaFiscal;
        _efectos = new EfectosPostTimbrado(db, sender, fiscal, cfdiRepo, eventos, receptorFiscal, productos);
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
        var fiscal = Millet.Facturacion.Application.Timbrado.CfdiEmisionBuilder.FechaFiscal(ahora); // período en la fecha fiscal de México, no en UTC
        if (!await _periodo.EstaAbiertoAsync(fiscal.Year, fiscal.Month, ct))
            throw new BusinessRuleException(
                "PERIODO_CERRADO",
                $"El período contable {fiscal.Year}-{fiscal.Month:D2} está cerrado; no se puede timbrar.");

        if (comprobante is ReciboPago recibo)
        {
            var fechaPagoFiscal = CfdiEmisionBuilder.FechaFiscal(recibo.FechaPago);
            if (!await _periodo.EstaAbiertoAsync(fechaPagoFiscal.Year, fechaPagoFiscal.Month, ct))
                throw new BusinessRuleException("PERIODO_CERRADO", $"El período {fechaPagoFiscal.Year}/{fechaPagoFiscal.Month:D2} está cerrado.");
        }
        if (_empresaFiscal is not null)
        {
            var emisor = await _empresaFiscal.ObtenerEmisorAsync(comprobante.EmpresaId, comprobante.SucursalId, ct)
                ?? throw new BusinessRuleException("EMISOR_NO_DISPONIBLE", "Revisa los datos fiscales de la empresa y que la sucursal emisora esté activa.");
            comprobante.ActualizarEmisorParaReintento(emisor);
        }
        await _receptorFiscal.ValidarComprobanteAsync(comprobante, ct);
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

    /// <summary>
    /// 86e3t97tx: guarda <c>TimbradoEnProceso</c> antes de llamar al PAC. El
    /// token <c>Version</c> (ADR-0012) hace que solo un reintento concurrente
    /// gane; el perdedor recibe <c>REINTENTO_EN_CURSO</c> sin tocar el PAC. Si
    /// algo falla después del PAC, el comprobante queda en proceso (no en un
    /// fallido "limpio") y el <c>TimbradoPendienteWorker</c> lo pasa a
    /// <c>PAC_TIMEOUT</c>, que exige confirmación para reintentar.
    /// </summary>
    private Task PersistirEnProcesoAsync(CancellationToken ct) =>
        TimbradoDurable.GuardarAsync(_db, "REINTENTO_EN_CURSO",
            "Otro reintento de timbrado de este comprobante ya está en curso. " +
            "Espera unos segundos y recarga para ver el resultado; no lo vuelvas a intentar.", ct);

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

        // CCE121: el reintento timbra con la fecha de hoy, así que el TipoCambioUSD es el del DOF que
        // rige hoy (catálogo), aunque la factura se haya guardado con otro valor (p. ej. capturado a mano).
        if (factura.ComplementoCce is { } cce && _catalogos is not null)
            cce.ActualizarTcDof(await Facturas.TipoCambioCce.DelDofAsync(_catalogos, ahora, ct));

        await TimbradoEjecutor.TimbrarYAplicarAsync(_db, 
            factura, CfdiEmisionBuilder.DesdeFacturaVenta(factura, ahora),
            _fiscal, _cfdiRepo, ahora, ct, PersistirEnProcesoAsync);

        // M-285: timbrada, sus efectos se registran y se aplican uno por uno (un fallo queda pendiente
        // y se reintenta aparte, sin deshacer el timbrado). En proceso, solo se toma el pedido (G5).
        if (factura.Estado == EstadoTimbrado.Timbrado)
        {
            await _efectos.RegistrarAsync(factura, await ClienteContableFacturacion.ResolverAsync(_db, factura, ct), ct);
            await _db.SaveChangesAsync(ct);
            await _efectos.EjecutarPendientesAsync(factura, ahora, ct);
        }
        else
            await _efectos.FacturaVentaAsync(factura, ahora, ct);

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
            _fiscal, _cfdiRepo, ahora, ct, PersistirEnProcesoAsync);

        await _efectos.FacturaAnticipoAsync(factura, ahora, ct);

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
            _fiscal, _cfdiRepo, ahora, ct, PersistirEnProcesoAsync);

        await _efectos.NotaCreditoAsync(nc, ahora, ct);

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
            _fiscal, _cfdiRepo, ahora, ct, PersistirEnProcesoAsync);

        await _efectos.ReciboPagoAsync(repp, ahora, ct);

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
            _fiscal, _cfdiRepo, ahora, ct, PersistirEnProcesoAsync);

        // EmitirCartaPorteHandler no publica eventos de integración.
        return "CartaPorte";
    }
}
