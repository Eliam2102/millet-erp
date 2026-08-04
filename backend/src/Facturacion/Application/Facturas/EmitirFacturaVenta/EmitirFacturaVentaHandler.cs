using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Administracion.Domain;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.NotasCredito;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Activos;
using Millet.Facturacion.Domain.Cce;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;

/// <summary>
/// Orquesta la emisión de una factura de venta (§7.1, §12.2 levantamiento):
/// candado de período (D13) → validación local previa de catálogos SAT (no
/// consumir timbres en errores corregibles) → reserva atómica de folio
/// (<c>Compartido.Series</c>, tipo <c>Cfdi</c>) → construcción del agregado →
/// timbrado contra el PAC (stub hasta F12) → persistencia.
///
/// <para>
/// La reserva de folio se ejecuta en su propia transacción (UPSERT atómico en
/// <c>compartido.secuencias_folio</c>); si la persistencia posterior falla, el
/// folio queda "quemado" (hueco aceptable — nunca duplicado, §5 cuidados-infra).
/// </para>
/// </summary>
public sealed class EmitirFacturaVentaHandler
    : IRequestHandler<EmitirFacturaVentaCommand, EmitirFacturaVentaResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ISender _sender;
    private readonly IPeriodoContablePort _periodo;
    private readonly ICatalogosSatReadPort _catalogos;
    private readonly ICfdiTimbradoPort _fiscal;
    private readonly ICfdiRepositorioPort _cfdiRepo;
    private readonly IEmpresaFiscalReadPort _empresasFiscal;
    private readonly IIntegrationEventPublisher _eventos;
    private readonly IContabilidadAsientoPort _contabilidad;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;

    public EmitirFacturaVentaHandler(
        FacturacionDbContext db,
        ISender sender,
        IPeriodoContablePort periodo,
        ICatalogosSatReadPort catalogos,
        ICfdiTimbradoPort fiscal,
        ICfdiRepositorioPort cfdiRepo,
        IEmpresaFiscalReadPort empresasFiscal,
        IIntegrationEventPublisher eventos,
        IContabilidadAsientoPort contabilidad,
        ICurrentEmpresaContext empresa,
        ICurrentUserContext user,
        IClock clock)
    {
        _db = db;
        _sender = sender;
        _periodo = periodo;
        _catalogos = catalogos;
        _fiscal = fiscal;
        _cfdiRepo = cfdiRepo;
        _empresasFiscal = empresasFiscal;
        _eventos = eventos;
        _contabilidad = contabilidad;
        _empresa = empresa;
        _user = user;
        _clock = clock;
    }

    public async Task<EmitirFacturaVentaResponse> Handle(
        EmitirFacturaVentaCommand command,
        CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "No hay empresa seleccionada en el contexto del request.");

        var ahora = _clock.UtcNow;
        var anio = ahora.Year;
        var mes = ahora.Month;

        // 1. Candado de período contable (D13).
        if (!await _periodo.EstaAbiertoAsync(anio, mes, cancellationToken))
            throw new BusinessRuleException(
                "PERIODO_CERRADO",
                $"El período contable {anio}-{mes:D2} está cerrado; no se puede emitir.");

        // 2. Validación local previa de catálogos SAT.
        await ValidarCatalogosSatAsync(command, cancellationToken);

        var receptor = new DatosFiscalesReceptor(
            Rfc: command.ReceptorRfc,
            Nombre: command.ReceptorNombre,
            RegimenFiscal: command.ReceptorRegimenFiscal,
            CodigoPostal: command.ReceptorCodigoPostal,
            UsoCfdi: command.ReceptorUsoCfdi,
            Pais: command.ReceptorPais,
            EsGenerico: DatosFiscalesReceptor.EsRfcGenerico(command.ReceptorRfc));

        // 2.bis F12-PR1: snapshot del emisor (razón social + LugarExpedicion del
        // master). Falla aquí si la empresa no tiene CP fiscal — sin quemar folio.
        var emisor = await EmisorSnapshot.ResolverAsync(
            _empresasFiscal, empresaId, command.RfcEmisor, command.RegimenFiscalEmisor, cancellationToken);

        // 3. F4-PR2: cargar y validar los anticipos a amortizar ANTES de timbrar —
        // la relación 07 va en el XML (inmutable) y un saldo insuficiente debe
        // abortar sin quemar el timbre.
        var anticipos = await CargarYValidarAnticiposAsync(command, receptor.Rfc, cancellationToken);

        // 3.bis F9: venta de activo fijo exige autorización del Contador General
        // (no se timbra sin ella). Se valida antes de reservar folio.
        var autorizacion = await CargarYValidarAutorizacionAsync(command, cancellationToken);

        // 3.ter B2: si se emite desde un pedido, debe estar Importado (re-facturable).
        var pedido = await CargarYValidarPedidoAsync(command, cancellationToken);

        // 4. Reserva atómica de folio (Cfdi) vía Compartido.Series.
        var reserva = await _sender.Send(
            new ReservarFolioCommand(
                empresaId,
                command.SucursalId,
                TipoDocumentoSerie.Cfdi,
                DateOnly.FromDateTime(ahora.UtcDateTime)),
            cancellationToken);

        // 5. Construir la factura (borrador) + líneas + totales.
        var factura = FacturaVenta.CrearBorrador(
            empresaId: empresaId,
            folio: reserva.Folio,
            folioNumero: reserva.Numero,
            sucursalId: command.SucursalId,
            // CAJAS-PR4 (§6): CajaId ya no viene del comando — lo asigna
            // RegistrarCobroMostrador vía AsignarCajaCobro (Capa B).
            cajaId: null,
            usuarioEmisorId: _user.UserId,
            receptor: receptor,
            emisor: emisor,
            metodoPago: command.MetodoPago,
            formaPago: command.FormaPago,
            moneda: command.Moneda,
            tipoCambio: command.TipoCambio,
            periodoAnio: anio,
            periodoMes: mes,
            canalVentaId: command.CanalVenta,
            comportamientoFiscal: command.ComportamientoFiscal,
            pedidoFacturableId: command.PedidoFacturableId,
            obraId: command.ObraId,
            obraNombre: command.ObraNombre,
            facturaAgrupada: command.FacturaAgrupada);

        foreach (var l in command.Lineas)
        {
            factura.AgregarLinea(
                productoId: l.ProductoId,
                claveProdServSat: l.ClaveProdServSat,
                descripcion: l.Descripcion,
                claveUnidadSat: l.ClaveUnidadSat,
                cantidad: l.Cantidad,
                valorUnitario: l.ValorUnitario,
                descuento: l.Descuento,
                objetoImp: l.ObjetoImp,
                tasaIvaTraslado: l.TasaIvaTraslado,
                tasaRetencionIva: l.TasaRetencionIva,
                tasaRetencionIsr: l.TasaRetencionIsr,
                requierePedimento: l.RequierePedimento);
        }

        factura.RecalcularTotales();

        // 5.bis F7-PR1: Complemento de Comercio Exterior (antes de timbrar — va en
        // el XML). AdjuntarCce valida que el comportamiento sea ExportacionConCce.
        if (command.Cce is { } cceDatos)
        {
            var cce = ComplementoCce.Crear(
                factura.Id, cceDatos.TipoOperacion, cceDatos.Incoterm, cceDatos.TcDof,
                cceDatos.ReceptorNumRegIdTrib, cceDatos.ReceptorPaisResidencia,
                cceDatos.ClaveDePedimento, cceDatos.CertificadoOrigen,
                cceDatos.ReceptorDomicilioCalle, cceDatos.ReceptorDomicilioEstado,
                cceDatos.ReceptorDomicilioCodigoPostal);
            foreach (var l in cceDatos.Lineas)
                cce.AgregarLinea(l.FraccionArancelaria, l.UnidadAduana, l.CantidadAduana, l.ValorUnitarioAduana, l.ValorDolares, l.AplicaIva0);
            factura.AdjuntarCce(cce);
        }

        // 5.quater F9: vincula + consume la autorización de venta de activo fijo.
        if (autorizacion is not null)
        {
            factura.VincularAutorizacion(autorizacion.Id);
            autorizacion.MarcarUsada(factura.Id);
        }

        // 5.ter F7-PR2: compuerta de pedimento (§3.bis.5, por factura, nunca
        // global). Si requiere pedimento y aún no lo tiene, se retiene sin timbrar
        // (solo esta factura); AplicarPedimentoCommand la completa después.
        if (factura.RequierePedimento && !factura.PedimentoCompleto)
        {
            factura.MarcarPendientePedimento();
            // B2/B3: el pedido queda tomado por la factura retenida (no re-facturable).
            pedido?.MarcarFacturado(factura.Id);
            _db.FacturasVenta.Add(factura);
            await _db.SaveChangesAsync(cancellationToken);
            return new EmitirFacturaVentaResponse(
                Id: factura.Id, Estado: factura.Estado.ToString(), Uuid: null,
                Folio: factura.Folio, Total: factura.Total, Version: factura.Version,
                NotasCreditoAmortizacion: null);
        }

        // 6. F4-PR2: relaciones 07 a las facturas de anticipo (deben ir en el XML,
        // por eso antes de timbrar).
        foreach (var a in anticipos)
            if (!string.IsNullOrWhiteSpace(a.FacturaAnticipo.Uuid))
                factura.AgregarRelacion(a.FacturaAnticipo.Uuid!, "07");

        // 7. Timbrar contra el PAC. Sellado + timbrado en una sola operación
        // (D11). Asíncrono-tolerante (Decisión 01-C); el ejecutor aplica la FSM
        // y persiste el XML en el repo común.
        await TimbradoEjecutor.TimbrarYAplicarAsync(_db, 
            factura,
            CfdiEmisionBuilder.DesdeFacturaVenta(factura, ahora),
            _fiscal, _cfdiRepo, ahora, cancellationToken);

        // 8. F4-PR2: amortizar (generar + timbrar una NC por anticipo) si la
        // factura quedó Timbrada. Atómico: cualquier fallo lanza y el SaveChanges
        // único de abajo no llega a ocurrir (rollback total, §3.bis.4).
        IReadOnlyList<NotaCreditoAmortizacionEmitida> ncResp = [];
        if (anticipos.Count > 0)
        {
            if (factura.Estado != EstadoTimbrado.Timbrado)
                throw new BusinessRuleException(
                    "FACTURA_NO_TIMBRADA_PARA_AMORTIZAR",
                    "No se pueden amortizar anticipos: la factura no quedó timbrada.");
            ncResp = await AmortizarAnticiposAsync(factura, receptor, anticipos, anio, mes, ahora, cancellationToken);
        }

        // 8.bis RANURA-PR2: NC automática por la ranura del pedido A+W
        // (relación 01) — la factura va por el total y la caja cobra
        // total − NC ([Decisión 13-K]). Solo si quedó Timbrada; si el timbre
        // falló, la emite el reintento (ReintentarTimbradoHandler). Si la NC
        // no timbra, NcRanuraEmisor lanza (rollback total, como amortización).
        var ncRanura = await NcRanuraEmisor.EmitirSiAplicaAsync(
            _db, _sender, _fiscal, _cfdiRepo, _eventos,
            factura, pedido, _user.UserId, ahora, cancellationToken);

        // 9. F10-PR1: evento de integración + asiento contable (al Outbox en la
        // misma TX vía el interceptor). Solo si quedó Timbrada.
        if (factura.Estado == EstadoTimbrado.Timbrado)
        {
            await _eventos.PublishAsync(new FacturaVentaTimbradaIntegrationEvent(
                factura.EmpresaId, ahora, factura.Id, factura.Uuid!, factura.Total, factura.Moneda, factura.PedidoFacturableId,
                factura.ReceptorRfc, factura.ReceptorNombre, factura.Folio, factura.MetodoPago, factura.FechaTimbrado),
                cancellationToken);
            await _contabilidad.RegistrarAsientoAsync(new AsientoContableSolicitud(
                factura.Id, "FacturaVenta", $"Factura {factura.Folio}", factura.Total, factura.Moneda, anio, mes), cancellationToken);
        }

        // [Decisión 01-G] G5: el pedido queda tomado por su factura desde el
        // PRIMER intento (1 pedido → 1 documento → N intentos), falle o no el
        // timbre. Un fallo se resuelve SOBRE la factura (reintentar-timbrado /
        // descartar), nunca emitiendo una segunda factura del mismo pedido.
        if (pedido is not null)
        {
            pedido.MarcarFacturado(factura.Id);

            // ADR-0048 D3 (PR5): pedidos A+W avisan de vuelta a la tabla-puente
            // (uuid + estado_facturacion) vía WriteBackResultadoWorker. En el
            // mismo commit que MarcarFacturado — cero ventana de inconsistencia.
            if (pedido.Origen == OrigenPedido.Aw && factura.Estado == EstadoTimbrado.Timbrado)
            {
                var control = await _db.IngestaControles
                    .FirstOrDefaultAsync(c => c.PedidoFacturableId == pedido.Id, cancellationToken);
                if (control is not null)
                {
                    control.SolicitarWriteBackEstado("Facturado", factura.Uuid, _clock.UtcNow);
                    control.CambiarEstado(EstadoIngesta.Facturado);
                }
            }
        }

        _db.FacturasVenta.Add(factura);
        await _db.SaveChangesAsync(cancellationToken);

        return new EmitirFacturaVentaResponse(
            Id: factura.Id,
            Estado: factura.Estado.ToString(),
            Uuid: factura.Uuid,
            Folio: factura.Folio,
            Total: factura.Total,
            Version: factura.Version,
            NotasCreditoAmortizacion: ncResp.Count > 0 ? ncResp : null,
            NotaCreditoRanura: ncRanura);
    }

    /// <summary>
    /// Carga los anticipos a amortizar con su CFDI y valida (antes de timbrar):
    /// existencia, estado Abierto, mismo cliente (RFC receptor) e importe ≤ saldo
    /// disponible. Un fallo aquí aborta sin quemar el timbre de la factura.
    /// </summary>
    /// <summary>
    /// Si el comportamiento es <c>VentaActivoFijo</c>, exige y valida la
    /// autorización del Contador General (existente y aún disponible). Para
    /// cualquier otro comportamiento devuelve <c>null</c>.
    /// </summary>
    private async Task<AutorizacionVentaActivo?> CargarYValidarAutorizacionAsync(
        EmitirFacturaVentaCommand command, CancellationToken cancellationToken)
    {
        if (command.ComportamientoFiscal != ComportamientoFiscal.VentaActivoFijo)
            return null;

        if (command.AutorizacionId is not Guid autorizacionId)
            throw new BusinessRuleException(
                "AUTORIZACION_REQUERIDA",
                "La venta de un activo fijo requiere la autorización del Contador General antes de timbrar.");

        var autorizacion = await _db.AutorizacionesVentaActivo
            .FirstOrDefaultAsync(a => a.Id == autorizacionId, cancellationToken)
            ?? throw new EntityNotFoundException("AUTORIZACION_NO_ENCONTRADA", $"No existe la autorización {autorizacionId}.");

        if (autorizacion.Estado != EstadoAutorizacionActivo.Autorizada)
            throw new BusinessRuleException(
                "AUTORIZACION_NO_DISPONIBLE",
                $"La autorización ya fue {autorizacion.Estado} y no puede reutilizarse.");

        return autorizacion;
    }

    /// <summary>
    /// Si la emisión es desde un pedido (B2), lo carga y exige que esté
    /// <c>Importado</c> (no Bloqueado/Facturado/Cancelado). Devuelve la entidad
    /// trackeada para marcarla <c>Facturado</c> al emitir; <c>null</c> si no hay
    /// pedido de origen.
    /// </summary>
    private async Task<PedidoFacturable?> CargarYValidarPedidoAsync(
        EmitirFacturaVentaCommand command, CancellationToken cancellationToken)
    {
        if (command.PedidoFacturableId is not Guid pedidoId)
            return null;

        var pedido = await _db.PedidosFacturables
            .FirstOrDefaultAsync(p => p.Id == pedidoId, cancellationToken)
            ?? throw new EntityNotFoundException("PEDIDO_NO_ENCONTRADO", $"No existe el pedido facturable {pedidoId}.");

        if (pedido.Estado != EstadoPedidoFacturable.Importado)
            throw new BusinessRuleException(
                "PEDIDO_NO_FACTURABLE",
                $"El pedido {pedidoId} no es facturable (estado actual: {pedido.Estado}; sólo Importado).");

        return pedido;
    }

    private async Task<List<AnticipoAmortizable>> CargarYValidarAnticiposAsync(
        EmitirFacturaVentaCommand command, string receptorRfc, CancellationToken cancellationToken)
    {
        if (command.Anticipos is not { Count: > 0 } solicitados)
            return [];

        var resultado = new List<AnticipoAmortizable>();
        foreach (var sol in solicitados)
        {
            var anticipo = await _db.Anticipos
                .Include(a => a.Vinculaciones)
                .FirstOrDefaultAsync(a => a.Id == sol.AnticipoId, cancellationToken)
                ?? throw new EntityNotFoundException("ANTICIPO_NO_ENCONTRADO", $"No existe el anticipo {sol.AnticipoId}.");

            if (!string.Equals(anticipo.ReceptorRfc, receptorRfc, StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException(
                    "ANTICIPO_CLIENTE_DISTINTO",
                    "El anticipo y la factura final deben ser del mismo cliente (RFC receptor).");
            if (sol.Importe <= 0)
                throw new BusinessRuleException("ANTICIPO_IMPORTE_INVALIDO", "El importe a amortizar debe ser mayor que cero.");
            if (sol.Importe > anticipo.SaldoDisponible)
                throw new BusinessRuleException(
                    "ANTICIPO_SALDO_INSUFICIENTE",
                    $"El importe a amortizar ({sol.Importe}) excede el saldo disponible ({anticipo.SaldoDisponible}) del anticipo {anticipo.Id}.");

            var facturaAnticipo = await _db.FacturasAnticipo
                .FirstOrDefaultAsync(f => f.Id == anticipo.FacturaAnticipoId, cancellationToken)
                ?? throw new EntityNotFoundException("FACTURA_ANTICIPO_NO_ENCONTRADA", $"No existe el CFDI del anticipo {anticipo.Id}.");

            // 13-J: la relación 07 al CFDI del anticipo es obligatoria en el XML
            // de la factura final y de la NC de amortización; sin UUID quedaría
            // una amortización sin rastro fiscal (caso VEN-000003, 2026-07-12).
            if (facturaAnticipo.Estado != EstadoTimbrado.Timbrado
                || string.IsNullOrWhiteSpace(facturaAnticipo.Uuid))
                throw new BusinessRuleException(
                    "ANTICIPO_CFDI_NO_TIMBRADO",
                    $"El CFDI del anticipo ({facturaAnticipo.Folio}) no está timbrado " +
                    $"(estado: {facturaAnticipo.Estado}); resuélvelo (reintentar o descartar) antes de amortizar.");

            resultado.Add(new AnticipoAmortizable(anticipo, facturaAnticipo, sol.Importe));
        }

        return resultado;
    }

    /// <summary>
    /// Por cada anticipo: reserva folio de NC, autogenera la NC de amortización
    /// (relación 07 a la factura de anticipo y a la factura final), la timbra
    /// (stub) y reduce el saldo del anticipo. Si una NC no queda Timbrada, lanza
    /// (rollback total). Tasa de IVA estándar para el desglose; el detalle fiscal
    /// exacto lo afina F12.
    /// </summary>
    private async Task<IReadOnlyList<NotaCreditoAmortizacionEmitida>> AmortizarAnticiposAsync(
        FacturaVenta factura,
        DatosFiscalesReceptor receptor,
        IReadOnlyList<AnticipoAmortizable> anticipos,
        int anio, int mes, DateTimeOffset ahora,
        CancellationToken cancellationToken)
    {
        const decimal tasaIvaEstandar = 0.16m;
        var emitidas = new List<NotaCreditoAmortizacionEmitida>();

        foreach (var a in anticipos)
        {
            var reservaNc = await _sender.Send(
                new ReservarFolioCommand(
                    factura.EmpresaId, factura.SucursalId,
                    TipoDocumentoSerie.NotaCredito, DateOnly.FromDateTime(ahora.UtcDateTime)),
                cancellationToken);

            var nc = NotaCredito.CrearAmortizacion(
                empresaId: factura.EmpresaId,
                folio: reservaNc.Folio,
                folioNumero: reservaNc.Numero,
                sucursalId: factura.SucursalId,
                cajaId: factura.CajaId,
                usuarioEmisorId: _user.UserId,
                receptor: receptor,
                emisor: factura.SnapshotEmisor(),
                formaPago: factura.FormaPago,
                moneda: factura.Moneda,
                tipoCambio: factura.TipoCambio,
                periodoAnio: anio,
                periodoMes: mes,
                // Hereda dimensiones de la factura final que la genera
                // ([Decisión 12-10]).
                canalVentaId: factura.CanalVentaId,
                anticipoOrigenId: a.Anticipo.Id,
                facturaRelacionadaId: factura.Id,
                montoTotal: a.Importe,
                tasaIva: tasaIvaEstandar);

            if (!string.IsNullOrWhiteSpace(a.FacturaAnticipo.Uuid))
                nc.AgregarRelacion(a.FacturaAnticipo.Uuid!, "07");
            if (!string.IsNullOrWhiteSpace(factura.Uuid))
                nc.AgregarRelacion(factura.Uuid!, "07");

            var timbre = await TimbradoEjecutor.TimbrarYAplicarAsync(_db, 
                nc, CfdiEmisionBuilder.DesdeNotaCredito(nc, ahora),
                _fiscal, _cfdiRepo, ahora, cancellationToken);

            if (timbre.Estado != TimbradoEstado.Timbrado || string.IsNullOrWhiteSpace(timbre.Uuid))
                throw new BusinessRuleException(
                    "NC_AMORTIZACION_NO_TIMBRADA",
                    $"La NC de amortización del anticipo {a.Anticipo.Id} no se timbró " +
                    $"({timbre.Estado}); se aborta la emisión (rollback total).");

            a.Anticipo.RegistrarAmortizacion(factura.Id, nc.Id, a.Importe, ahora);
            _db.NotasCredito.Add(nc);

            // F10-PR1: evento de NC de amortización timbrada.
            await _eventos.PublishAsync(new NotaCreditoTimbradaIntegrationEvent(
                nc.EmpresaId, ahora, nc.Id, nc.Motivo.ToString(), nc.Uuid!, nc.Total, factura.Id, a.Anticipo.Id),
                cancellationToken);

            emitidas.Add(new NotaCreditoAmortizacionEmitida(
                Id: nc.Id, AnticipoId: a.Anticipo.Id, Folio: nc.Folio, Uuid: nc.Uuid,
                Total: nc.Total, SaldoAnticipoRestante: a.Anticipo.Saldo));
        }

        return emitidas;
    }

    /// <summary>Anticipo cargado para amortización: su saldo, su CFDI (para el UUID) y el importe a aplicar.</summary>
    private sealed record AnticipoAmortizable(
        Millet.Facturacion.Domain.Anticipos.Anticipo Anticipo,
        Millet.Facturacion.Domain.Anticipos.FacturaAnticipo FacturaAnticipo,
        decimal Importe);

    private async Task ValidarCatalogosSatAsync(
        EmitirFacturaVentaCommand command,
        CancellationToken cancellationToken)
    {
        if (!await _catalogos.ExisteMonedaAsync(command.Moneda, cancellationToken))
            throw new BusinessRuleException("MONEDA_INVALIDA", $"La moneda '{command.Moneda}' no existe en el catálogo SAT.");
        if (!await _catalogos.ExisteFormaPagoAsync(command.FormaPago, cancellationToken))
            throw new BusinessRuleException("FORMA_PAGO_INVALIDA", $"La forma de pago '{command.FormaPago}' no existe en el catálogo SAT.");
        if (!await _catalogos.ExisteUsoCfdiAsync(command.ReceptorUsoCfdi, cancellationToken))
            throw new BusinessRuleException("USO_CFDI_INVALIDO", $"El uso CFDI '{command.ReceptorUsoCfdi}' no existe en el catálogo SAT.");
        if (!await _catalogos.ExisteRegimenFiscalAsync(command.RegimenFiscalEmisor, cancellationToken))
            throw new BusinessRuleException("REGIMEN_EMISOR_INVALIDO", $"El régimen fiscal del emisor '{command.RegimenFiscalEmisor}' no existe en el catálogo SAT.");
    }

}
