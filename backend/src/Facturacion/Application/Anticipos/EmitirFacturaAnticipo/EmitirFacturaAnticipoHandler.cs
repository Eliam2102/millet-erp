using MediatR;
using Millet.Administracion.Application.Series;
using Millet.Administracion.Domain;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;

/// <summary>
/// Orquesta la emisión de una factura de anticipo (Momento 1, §6.2): candado de
/// período (D13) → validación de catálogos SAT → reserva de folio FANT
/// (<c>TipoDocumentoSerie.FacturaAnticipo</c>) → construcción del CFDI →
/// timbrado (stub hasta F12) → apertura del saldo (<c>Anticipo</c>). El CFDI y
/// el saldo se persisten en la misma transacción.
///
/// <para>
/// Reusa el mismo patrón que <c>EmitirFacturaVentaHandler</c> (folio en su propia
/// TX UPSERT; el archivo CFDI va al repo común de Integraciones.Fiscal).
/// </para>
/// </summary>
public sealed class EmitirFacturaAnticipoHandler
    : IRequestHandler<EmitirFacturaAnticipoCommand, EmitirFacturaAnticipoResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ISender _sender;
    private readonly IPeriodoContablePort _periodo;
    private readonly ICatalogosSatReadPort _catalogos;
    private readonly ICfdiTimbradoPort _fiscal;
    private readonly ICfdiRepositorioPort _cfdiRepo;
    private readonly IEmpresaFiscalReadPort _empresasFiscal;
    private readonly IIntegrationEventPublisher _eventos;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;

    public EmitirFacturaAnticipoHandler(
        FacturacionDbContext db,
        ISender sender,
        IPeriodoContablePort periodo,
        ICatalogosSatReadPort catalogos,
        ICfdiTimbradoPort fiscal,
        ICfdiRepositorioPort cfdiRepo,
        IEmpresaFiscalReadPort empresasFiscal,
        IIntegrationEventPublisher eventos,
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
        _empresa = empresa;
        _user = user;
        _clock = clock;
    }

    public async Task<EmitirFacturaAnticipoResponse> Handle(
        EmitirFacturaAnticipoCommand command,
        CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "No hay empresa seleccionada en el contexto del request.");

        var ahora = _clock.UtcNow;
        var anio = ahora.Year;
        var mes = ahora.Month;

        // 1. Candado de período (D13).
        if (!await _periodo.EstaAbiertoAsync(anio, mes, cancellationToken))
            throw new BusinessRuleException(
                "PERIODO_CERRADO",
                $"El período contable {anio}-{mes:D2} está cerrado; no se puede emitir.");

        // 2. Validación local de catálogos SAT (no quemar timbres en error corregible).
        await ValidarCatalogosSatAsync(command, cancellationToken);

        var receptor = new DatosFiscalesReceptor(
            Rfc: command.ReceptorRfc,
            Nombre: command.ReceptorNombre,
            RegimenFiscal: command.ReceptorRegimenFiscal,
            CodigoPostal: command.ReceptorCodigoPostal,
            UsoCfdi: command.ReceptorUsoCfdi,
            Pais: command.ReceptorPais,
            EsGenerico: DatosFiscalesReceptor.EsRfcGenerico(command.ReceptorRfc));

        // 2.bis F12-PR1: snapshot del emisor (falla sin CP fiscal, sin quemar folio).
        var emisor = await EmisorSnapshot.ResolverAsync(
            _empresasFiscal, empresaId, command.RfcEmisor, command.RegimenFiscalEmisor, cancellationToken);

        // 3. Reserva atómica de folio de la serie de anticipos (FANT).
        var reserva = await _sender.Send(
            new ReservarFolioCommand(
                empresaId,
                command.SucursalId,
                TipoDocumentoSerie.FacturaAnticipo,
                DateOnly.FromDateTime(ahora.UtcDateTime)),
            cancellationToken);

        // 4. Construir el CFDI de anticipo (la factory rechaza receptor genérico).
        var anticipoId = Guid.CreateVersion7();
        var factura = FacturaAnticipo.CrearBorrador(
            empresaId: empresaId,
            folio: reserva.Folio,
            folioNumero: reserva.Numero,
            sucursalId: command.SucursalId,
            // CAJAS-PR4 (§6): CajaId ya no viene del comando — lo asigna
            // RegistrarCobroMostrador vía AsignarCajaCobro ([12-9]).
            cajaId: null,
            usuarioEmisorId: _user.UserId,
            receptor: receptor,
            emisor: emisor,
            formaPago: command.FormaPago,
            moneda: command.Moneda,
            tipoCambio: command.TipoCambio,
            periodoAnio: anio,
            periodoMes: mes,
            tipoAnticipo: command.TipoAnticipo,
            pedidoFacturableId: command.PedidoFacturableId,
            anticipoId: anticipoId,
            montoBase: command.MontoBase,
            tasaIvaTraslado: command.TasaIvaTraslado,
            descripcion: command.Descripcion,
            canalVentaId: command.CanalVentaId);

        // 5. Timbrar contra el PAC. Asíncrono-tolerante; el ejecutor aplica la
        // FSM y persiste el XML en el repo común.
        await TimbradoEjecutor.TimbrarYAplicarAsync(_db, 
            factura,
            CfdiEmisionBuilder.DesdeFacturaAnticipo(factura, ahora),
            _fiscal, _cfdiRepo, ahora, cancellationToken);

        // 6. Abrir el saldo amortizable. PUE cobra el total al emitir; PPD arranca
        //    en 0 (lo llenan los REPP, F6).
        var montoCobrado = EsPue(command.MetodoPago) ? factura.Total : 0m;
        var anticipo = Anticipo.Crear(
            empresaId: empresaId,
            clienteId: command.ClienteId,
            receptorRfc: command.ReceptorRfc,
            tipoAnticipo: command.TipoAnticipo,
            moneda: command.Moneda,
            montoCobrado: montoCobrado,
            facturaAnticipoId: factura.Id,
            pedidoOrigenRef: command.PedidoOrigenRef,
            obraId: command.ObraId,
            obraNombre: command.ObraNombre,
            id: anticipoId);

        // F10-PR1: evento de integración (asiento de anticipo MXP/USD).
        if (factura.Estado == EstadoTimbrado.Timbrado)
            await _eventos.PublishAsync(new FacturaAnticipoTimbradaIntegrationEvent(
                factura.EmpresaId, ahora, factura.Id, anticipo.Id, factura.Uuid!, factura.Total, factura.Moneda),
                cancellationToken);

        _db.FacturasAnticipo.Add(factura);
        _db.Anticipos.Add(anticipo);
        await _db.SaveChangesAsync(cancellationToken);

        return new EmitirFacturaAnticipoResponse(
            AnticipoId: anticipo.Id,
            FacturaAnticipoId: factura.Id,
            Estado: factura.Estado.ToString(),
            Uuid: factura.Uuid,
            Folio: factura.Folio,
            Total: factura.Total,
            Saldo: anticipo.Saldo);
    }

    private static bool EsPue(string metodoPago) =>
        string.Equals(metodoPago, "PUE", StringComparison.OrdinalIgnoreCase);

    private async Task ValidarCatalogosSatAsync(
        EmitirFacturaAnticipoCommand command,
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
