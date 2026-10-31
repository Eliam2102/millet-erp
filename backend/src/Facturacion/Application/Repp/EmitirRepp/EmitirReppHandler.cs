using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Administracion.Domain;
using Millet.Facturacion.Application.Facturas;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Repp.EmitirRepp;

/// <summary>
/// Orquesta la emisión de un REPP: valida las facturas (timbradas, PPD, mismo
/// cliente), calcula parcialidad y saldo por factura (REPP previos + NC
/// timbradas que la acreditan, [Decisión 13-K]) y la diferencia cambiaria,
/// construye el CFDI tipo P y lo timbra (stub hasta F12).
/// </summary>
public sealed class EmitirReppHandler : IRequestHandler<EmitirReppCommand, EmitirReppResponse>
{
    private readonly ValidadorReceptorFiscal _receptorFiscal;
    private readonly FacturacionDbContext _db;
    private readonly ISender _sender;
    private readonly IPeriodoContablePort _periodo;
    private readonly ICfdiTimbradoPort _fiscal;
    private readonly ICfdiRepositorioPort _cfdiRepo;
    private readonly IIntegrationEventPublisher _eventos;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;
    private readonly ICuentaBancariaMilletReadPort _cuentasMillet;

    public EmitirReppHandler(
        FacturacionDbContext db, ISender sender, IPeriodoContablePort periodo, ICfdiTimbradoPort fiscal,
        ICfdiRepositorioPort cfdiRepo, IIntegrationEventPublisher eventos, ICurrentEmpresaContext empresa,
        ICurrentUserContext user, IClock clock, ValidadorReceptorFiscal receptorFiscal,
        ICuentaBancariaMilletReadPort cuentasMillet)
    {
        _cuentasMillet = cuentasMillet;
        _receptorFiscal = receptorFiscal;
        _db = db;
        _sender = sender;
        _periodo = periodo;
        _fiscal = fiscal;
        _cfdiRepo = cfdiRepo;
        _eventos = eventos;
        _empresa = empresa;
        _user = user;
        _clock = clock;
    }

    public async Task<EmitirReppResponse> Handle(EmitirReppCommand command, CancellationToken cancellationToken)
    {
        // FAC-03: el bloqueo por factura solo sirve dentro de una transacción; la bandeja ya abre la
        // suya, «Nuevo REPP» no. Se abre aquí si no hay (se confirma al final; si falla, se revierte).
        await using var tx = _db.Database.IsRelational() && _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
        var respuesta = await EmitirAsync(command, cancellationToken);
        if (tx is not null) await tx.CommitAsync(cancellationToken);
        return respuesta;
    }

    private async Task<EmitirReppResponse> EmitirAsync(EmitirReppCommand command, CancellationToken cancellationToken)
    {
        // El claim del request gana; command.EmpresaId solo aplica en
        // invocaciones internas sin HTTP; Tesorería solo crea pendientes.
        if ((_empresa.Current ?? command.EmpresaId) is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");

        var ahora = _clock.UtcNow;
        var anio = ahora.Year;
        var mes = ahora.Month;

        if (!await _periodo.EstaAbiertoAsync(anio, mes, cancellationToken))
            throw new BusinessRuleException("PERIODO_CERRADO", $"El período contable {anio}-{mes:D2} está cerrado; no se puede emitir.");

        // La fecha fiscal se interpreta en México, aunque la persistencia use UTC.
        var fechaPagoFiscal = CfdiEmisionBuilder.FechaFiscal(command.FechaPago);
        if (!await _periodo.EstaAbiertoAsync(fechaPagoFiscal.Year, fechaPagoFiscal.Month, cancellationToken))
            throw new BusinessRuleException("PERIODO_CERRADO",
                $"El período {fechaPagoFiscal.Year}/{fechaPagoFiscal.Month:D2} está cerrado.");

        // 86e3tfdve: cuentas del pago validadas contra c_FormaPago antes de reservar folio.
        var (cuentaOrdenante, cuentaBeneficiaria) = await ResolverCuentasAsync(command, cancellationToken);

        // Cargar y validar las facturas cubiertas (timbradas, PPD, mismo cliente).
        var ids = command.Facturas.Select(f => f.FacturaVentaId).ToList();
        // FAC-03: mismo bloqueo por factura (en orden) que la bandeja, antes de leer saldo y parcialidad.
        if (_db.Database.IsRelational())
            foreach (var facturaId in ids.Distinct().Order())
                await _db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM facturacion.comprobante WHERE id = {facturaId} AND empresa_id = {empresaId} FOR UPDATE").ToListAsync(cancellationToken);
        var facturas = await _db.FacturasVenta
            .Include(f => f.Lineas) // ImpuestosDR del complemento de pago se arma desde las líneas
            .Where(f => ids.Contains(f.Id))
            .ToListAsync(cancellationToken);
        if (facturas.Count != ids.Count)
            throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", "Alguna factura del pago no existe.");

        var rfcReceptor = facturas[0].ReceptorRfc;
        foreach (var f in facturas)
        {
            if (f.Estado != EstadoTimbrado.Timbrado)
                throw new BusinessRuleException("FACTURA_NO_TIMBRADA", $"La factura {f.Id} no está timbrada (estado {f.Estado}).");
            if (f.MetodoPago != "PPD")
                throw new BusinessRuleException("REPP_FACTURA_NO_PPD", $"La factura {f.Id} no es PPD; no requiere complemento de pago.");
            if (!string.Equals(f.ReceptorRfc, rfcReceptor, StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("REPP_MULTIPLES_CLIENTES", "Todas las facturas del REPP deben ser del mismo cliente.");
        }

        var receptorFactura = facturas[0];
        var receptor = new DatosFiscalesReceptor(
            receptorFactura.ReceptorRfc, receptorFactura.ReceptorNombre, receptorFactura.ReceptorRegimenFiscal,
            receptorFactura.ReceptorCodigoPostal, receptorFactura.ReceptorUsoCfdi, receptorFactura.ReceptorPais,
            receptorFactura.ReceptorEsGenerico);

        await _receptorFiscal.ValidarComprobanteAsync(receptorFactura, cancellationToken, esRep: true);

        var reserva = await _sender.Send(
            new ReservarFolioCommand(empresaId, command.SucursalId, TipoDocumentoSerie.Cfdi, DateOnly.FromDateTime(ahora.UtcDateTime)),
            cancellationToken);

        // CAJAS-PR4 (§6): CajaId ya no viene del comando — lo asigna
        // RegistrarCobroMostrador vía AsignarCajaCobro (PPD cobrado en mostrador).
        var repp = ReciboPago.CrearBorrador(
            empresaId, reserva.Folio, reserva.Numero, command.SucursalId, cajaId: null, _user.UserId,
            receptor, receptorFactura.SnapshotEmisor(), anio, mes,
            command.FechaPago, command.MonedaPago, command.CanalVentaId);

        // [Decisión 13-K]: las NC timbradas (amortización de anticipos relación 07
        // y NC generales) acreditan al saldo por cobrar — el saldo del complemento
        // de pago las resta; nunca se reembolsan en efectivo.
        var acreditados = await SaldoPorCobrar.AcreditadoPorFacturaAsync(_db, ids, cancellationToken);

        decimal totalPago = 0m;
        foreach (var pago in command.Facturas)
        {
            var factura = facturas.Single(f => f.Id == pago.FacturaVentaId);

            // Parcialidad y saldo anterior: REPP previos vigentes (un complemento
            // Cancelado/Descartada no descuenta saldo ni consume parcialidad) +
            // NC que acreditan.
            var pagosPrevios = await SaldoPorCobrar.PagosVigentes(_db)
                .Where(p => p.FacturaVentaId == factura.Id)
                .ToListAsync(cancellationToken);
            var numParcialidad = pagosPrevios.Count + 1;
            var acreditado = acreditados.GetValueOrDefault(factura.Id);
            var pagado = pagosPrevios.Sum(p => p.ImportePagado);
            var saldoAnterior = factura.Total - acreditado - pagado;
            if (saldoAnterior <= 0)
                throw new BusinessRuleException(
                    "REPP_FACTURA_SIN_SALDO",
                    $"La factura {factura.Folio} no tiene saldo por cobrar (total {factura.Total}, " +
                    $"acreditado por notas de crédito {acreditado}, pagado {pagado}).");

            var objetoImpDR = factura.Lineas.Any(l => l.ObjetoImp == "02") ? "02" : "01";

            repp.AgregarFacturaPagada(
                facturaVentaId: factura.Id,
                facturaUuid: factura.Uuid ?? string.Empty,
                numParcialidad: numParcialidad,
                monedaFactura: factura.Moneda,
                importePagado: pago.ImportePagado,
                saldoAnterior: saldoAnterior,
                formaPagoReal: command.FormaPagoReal,
                tcFactura: factura.TipoCambio,
                tcPago: command.TcPago,
                cuentaOrdenante: cuentaOrdenante,
                cuentaBeneficiaria: cuentaBeneficiaria,
                referenciaPago: command.ReferenciaPago,
                objetoImpDR: objetoImpDR,
                facturaTotal: factura.Total,
                impuestosFactura: objetoImpDR == "02" ? ImpuestosDeFactura(factura) : []);

            totalPago += pago.ImportePagado;
        }

        repp.EstablecerImporteTotalPago(totalPago);

        // Timbrar; el ejecutor aplica la FSM y persiste el XML en el repo común.
        await TimbradoEjecutor.TimbrarYAplicarAsync(_db, 
            repp, CfdiEmisionBuilder.DesdeReciboPago(repp, ahora),
            _fiscal, _cfdiRepo, ahora, cancellationToken);

        // F10-PR1: evento de REPP timbrado (cobro confirmado; ganancia/pérdida cambiaria).
        if (repp.Estado == EstadoTimbrado.Timbrado)
            await _eventos.PublishAsync(new ReciboPagoTimbradoIntegrationEvent(
                repp.EmpresaId, ahora, repp.Id, repp.Uuid!, repp.ImporteTotalPago,
                repp.FacturasPagadas.Sum(f => f.GananciaPerdidaCambiaria),
                repp.FacturasPagadas.Select(f => new ReppFacturaPagadaDetalle(
                    f.FacturaVentaId, f.ImportePagado, f.NumParcialidad, f.MonedaFactura, f.SaldoInsoluto)).ToList(),
                command.Pendiente?.MovimientoBancarioId, command.Pendiente?.PropuestaId), cancellationToken);

        command.Pendiente?.RegistrarIntento(repp);
        _db.RecibosPago.Add(repp);
        await _db.SaveChangesAsync(cancellationToken);

        return new EmitirReppResponse(
            Id: repp.Id,
            Estado: repp.Estado.ToString(),
            Uuid: repp.Uuid,
            Folio: repp.Folio,
            ImporteTotalPago: repp.ImporteTotalPago,
            GananciaPerdidaCambiariaTotal: repp.FacturasPagadas.Sum(f => f.GananciaPerdidaCambiaria),
            Facturas: repp.FacturasPagadas
                .Select(f => new ReppFacturaPagada(f.FacturaVentaId, f.NumParcialidad, f.ImportePagado, f.SaldoInsoluto, f.GananciaPerdidaCambiaria))
                .ToList());
    }

    /// <summary>
    /// Normaliza las cuentas capturadas y, si no viene beneficiaria pero sí la
    /// cuenta de Millet elegida, la resuelve (CLABE o número según la forma).
    /// Valida ambas contra c_FormaPago (<see cref="CuentasPagoSat"/>).
    /// </summary>
    private async Task<(string? Ordenante, string? Beneficiaria)> ResolverCuentasAsync(
        EmitirReppCommand command, CancellationToken cancellationToken)
    {
        var ordenante = CuentasPagoSat.Normalizar(command.CuentaOrdenante);
        var beneficiaria = CuentasPagoSat.Normalizar(command.CuentaBeneficiaria);
        if (beneficiaria is null && command.CuentaBancariaBeneficiariaId is Guid cuentaId)
        {
            if (!CuentasPagoSat.AdmiteBeneficiaria(command.FormaPagoReal))
                throw new BusinessRuleException(CuentasPagoSat.CodigoError,
                    $"La forma de pago {command.FormaPagoReal} no admite cuenta beneficiaria; quita la cuenta de Millet o cambia la forma de pago.");
            var cuenta = await _cuentasMillet.ObtenerAsync(cuentaId, cancellationToken)
                ?? throw new BusinessRuleException("REPP_CUENTA_BANCARIA_NO_ENCONTRADA",
                    "La cuenta bancaria de Millet elegida no existe o no pertenece a la empresa.");
            beneficiaria = CuentasPagoSat.SugerirBeneficiaria(command.FormaPagoReal, cuenta.Clabe, cuenta.NumeroCuenta)
                ?? throw new BusinessRuleException(CuentasPagoSat.CodigoError,
                    $"La cuenta {cuenta.Banco} {CuentasPagoSat.Enmascarar(cuenta.NumeroCuenta)} no tiene CLABE ni número con el formato " +
                    $"del SAT para la forma de pago {command.FormaPagoReal}; captura la cuenta beneficiaria a mano.");
        }
        CuentasPagoSat.Validar(command.FormaPagoReal, ordenante, beneficiaria);
        return (ordenante, beneficiaria);
    }

    /// <summary>
    /// Estructura de impuestos de la factura pagada para el ImpuestosDR del
    /// complemento de pago, agrupada por (impuesto, tasa). El REPP la prorratea
    /// al importe pagado. Base gravable por línea = Importe − Descuento.
    /// c_Impuesto: 002 IVA, 001 ISR. Solo líneas objeto de impuesto ("02").
    /// </summary>
    private static List<ImpuestoFacturaPagada> ImpuestosDeFactura(FacturaVenta factura)
    {
        var lineas = factura.Lineas;
        var grupos = new List<ImpuestoFacturaPagada>();

        // IVA trasladado (002) por tasa — 0% incluido (Tasa 0.000000).
        foreach (var g in lineas.Where(l => l.ObjetoImp == "02" && l.TasaIvaTraslado is not null)
                                 .GroupBy(l => l.TasaIvaTraslado!.Value))
            grupos.Add(new ImpuestoFacturaPagada("002", "Tasa", g.Key, EsRetencion: false,
                BaseGravable: g.Sum(l => l.Importe - l.Descuento)));

        // Retención IVA (002).
        foreach (var g in lineas.Where(l => l.TasaRetencionIva is > 0m)
                                 .GroupBy(l => l.TasaRetencionIva!.Value))
            grupos.Add(new ImpuestoFacturaPagada("002", "Tasa", g.Key, EsRetencion: true,
                BaseGravable: g.Sum(l => l.Importe - l.Descuento)));

        // Retención ISR (001).
        foreach (var g in lineas.Where(l => l.TasaRetencionIsr is > 0m)
                                 .GroupBy(l => l.TasaRetencionIsr!.Value))
            grupos.Add(new ImpuestoFacturaPagada("001", "Tasa", g.Key, EsRetencion: true,
                BaseGravable: g.Sum(l => l.Importe - l.Descuento)));

        return grupos;
    }
}
