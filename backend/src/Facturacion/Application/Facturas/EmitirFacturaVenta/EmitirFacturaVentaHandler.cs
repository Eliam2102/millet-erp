using MediatR;
using Microsoft.Extensions.Logging;
using Millet.ComercioExterior.Application.PublicPorts;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Administracion.Domain;
using Millet.Facturacion.Application.Anticipos;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.NotasCredito;
using Millet.Facturacion.Application.Pedidos;
using Millet.Facturacion.Application.Sustituciones;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Activos;
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
/// (<c>Compartido.Series</c>, tipo <c>Cfdi</c>; <c>VentaDirecta</c> para pedidos
/// manuales clasificados, F6.1) → construcción del agregado →
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
    private readonly ValidadorReceptorFiscal _receptorFiscal;
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
    private readonly IProductosReadPort? _productos;
    private readonly IPlazoCreditoClienteReadPort? _plazoCredito;
    private readonly IEmbarqueExportacionReadPort? _embarques;
    private readonly ILogger<EmitirFacturaVentaHandler>? _logger;

    public EmitirFacturaVentaHandler(
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
        IClock clock, ValidadorReceptorFiscal receptorFiscal,
        // U1.6: tipo A+W de las líneas en el evento contable; opcional para no
        // romper composiciones existentes (sin puerto, TipoProducto = null).
        IProductosReadPort? productos = null,
        IEmbarqueExportacionReadPort? embarques = null,
        ILogger<EmitirFacturaVentaHandler>? logger = null,
        IPlazoCreditoClienteReadPort? plazoCredito = null)
    {
        _receptorFiscal = receptorFiscal;
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
        _productos = productos;
        _plazoCredito = plazoCredito;
        _embarques = embarques;
        _logger = logger;
    }

    // B2: estado de la emisión en curso para dejar rastro del folio si aborta después de reservarlo.
    // El handler es transitorio (uno por solicitud).
    private ReservarFolioResponse? _folioReservado;
    private Func<FacturaVenta>? _armarFactura;
    private FacturaVenta? _facturaEnCurso;
    private bool _facturaPersistida;
    private bool _pacRespondio;

    public async Task<EmitirFacturaVentaResponse> Handle(
        EmitirFacturaVentaCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return await HandleAsync(command, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && DebeDejarFolioDescartado())
        {
            await GuardarFolioDescartadoAsync(ex);
            throw;
        }
    }

    private async Task<EmitirFacturaVentaResponse> HandleAsync(
        EmitirFacturaVentaCommand command,
        CancellationToken cancellationToken)
    {
        // FAC-08 Fase 1: la factura de la salida de un contenedor se compuerta con ese contenedor, no con el 100 % del pedido.
        var contenedorId = command.SalidaFacturableId is Guid salidaCe && _embarques is not null
            ? await _db.SalidasFacturables.Where(s => s.Id == salidaCe && s.Origen == OrigenSalidaFacturable.ContenedorExportacion)
                .Select(s => s.OrigenId).FirstOrDefaultAsync(cancellationToken)
            : null;
        var embarque = command.PedidoFacturableId is Guid id && _embarques is not null
            ? await _embarques.ObtenerAsync(id, contenedorId, cancellationToken) : null;
        if (embarque?.Ligado == true)
        {
            EmitirFacturaVentaResponse? resultado = null;
            await PedidoEmbarqueLock.ExecuteAsync(_db, [command.PedidoFacturableId!.Value], async ct =>
            {
                var actual = await _embarques!.ObtenerAsync(command.PedidoFacturableId.Value, contenedorId, ct);
                resultado = await EmitirAsync(command, actual, ct);
            }, cancellationToken);
            return resultado!;
        }
        return await EmitirAsync(command, embarque, cancellationToken);
    }

    private async Task<EmitirFacturaVentaResponse> EmitirAsync(EmitirFacturaVentaCommand command,
        EmbarqueExportacionLectura? embarque, CancellationToken cancellationToken)
    {
        DatosEmbarqueCe? copiaCe = null;
        if (command.PedidoFacturableId is Guid pedidoCe && embarque is not null)
        {
            if (embarque.Ligado)
            {
                if (!embarque.ListoParaFacturar)
                    throw new BusinessRuleException(embarque.CodigoBloqueo ?? "FAC_PEDIDO_NO_EMBARCADO", embarque.Motivo ?? "Complete el embarque en Comercio Exterior.");
                if (embarque.SucursalId != command.SucursalId)
                    throw new BusinessRuleException("CE_SUCURSAL_DISTINTA", "La factura debe emitirse en la sucursal del expediente.");
                _logger?.LogInformation("Pedido {PedidoId}: campos de embarque y CCE tomados de CE {ExpedienteId}; clave e Incoterm enviados por el usuario ignorados", pedidoCe, embarque.ExpedienteId);
                if (string.IsNullOrWhiteSpace(embarque.Incoterm))
                    throw new BusinessRuleException("CE_INCOTERM_REQUERIDO", "Seleccione un Incoterm vigente en el expediente.");
                copiaCe = new(embarque.FolioExpediente!, embarque.NumeroPedimento!, embarque.ClavePedimento!, embarque.FechaPedimento!.Value,
                    embarque.Incoterm, Array.AsReadOnly(embarque.Contenedores!.Select(c => new ContenedorFacturaCe(c.Numero, c.BlGuia, c.FechaEmbarqueReal)).ToArray()), embarque.Aduana, embarque.AduanaNombre, embarque.ExpedienteId, embarque.VersionEmbarque, pedidoCe);
                if (embarque.ClavePedimento == "RT")
                {
                    command = command with { Cce = null };
                    var comportamientoPedido = await _db.PedidosFacturables
                        .Where(p => p.Id == pedidoCe).Select(p => p.ComportamientoFiscal).SingleAsync(cancellationToken);
                    if (comportamientoPedido == ComportamientoFiscal.ExportacionConCce)
                        throw new BusinessRuleException("FAC_RT_POR_CONFIRMAR", "La exportación RT sin complemento requiere confirmación de Fiscal (VAL-CE-08).");
                    command = command with { ComportamientoFiscal = comportamientoPedido };
                }
                else
                {
                    if (command.Cce is null) throw new BusinessRuleException("FAC_CCE_REQUERIDO", "Complete las mercancías y el receptor del CCE para la exportación A1.");
                    command = command with { ComportamientoFiscal = ComportamientoFiscal.ExportacionConCce,
                        Cce = command.Cce with { ClaveDePedimento = "A1", Incoterm = embarque.Incoterm,
                            Lineas = DatosAduanalesFactura.Aplicar(command.Cce.Lineas, embarque.DatosAduanales) } };
                }
            }
        }

        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "No hay empresa seleccionada en el contexto del request.");

        var ahora = _clock.UtcNow;
        // Período en la fecha fiscal de México: después de las 18:00 del último día, UTC ya es el mes siguiente.
        var anio = Millet.Facturacion.Application.Timbrado.CfdiEmisionBuilder.FechaFiscal(ahora).Year;
        var mes = Millet.Facturacion.Application.Timbrado.CfdiEmisionBuilder.FechaFiscal(ahora).Month;

        // 1. Candado de período contable (D13).
        if (!await _periodo.EstaAbiertoAsync(anio, mes, cancellationToken))
            throw new BusinessRuleException(
                "PERIODO_CERRADO",
                $"El período contable {anio}-{mes:D2} está cerrado; no se puede emitir.");

        await ValidadorProductoFiscal.ValidarAsync(command.Lineas.Select((l,i)=>new LineaFiscalAValidar(i+1,l.ProductoId,l.Descripcion,
            l.ClaveProdServSat,l.ClaveUnidadSat,l.ObjetoImp,l.TasaIvaTraslado)),_productos,cancellationToken);
        // 2. Validación local previa de catálogos SAT.
        await ValidarCatalogosSatAsync(command, cancellationToken);
        // B5: domicilio del receptor y clave de pedimento del CCE, antes de reservar folio.
        if (command.Cce is { } cceSat)
            Millet.Facturacion.Application.Timbrado.CfdiEmisionBuilder.ValidarDatosSatCce(
                cceSat.ClaveDePedimento, cceSat.ReceptorDomicilioCalle,
                cceSat.ReceptorDomicilioEstado, cceSat.ReceptorDomicilioCodigoPostal);
        await TipoCambioFactura.ValidarAsync(
            _catalogos, command.Moneda, command.TipoCambio, ahora, cancellationToken);

        var receptor = FacturaVentaArmado.ReceptorDe(command);

        // 2.ter FAC-08b: la original debe poder sustituirse ANTES de quemar folio o timbre.
        FacturaVenta? sustituida = null;
        if (command.SustituyeComprobanteId is Guid sustituyeId)
        {
            if (command.PedidoFacturableId is not null || command.SalidaFacturableId is not null
                || command.Anticipos is { Count: > 0 } || command.Cce is not null || command.AutorizacionId is not null)
                throw new BusinessRuleException(
                    "SUSTITUCION_COMBINACION_NO_SOPORTADA",
                    "La factura sustituta no se liga a pedido, salida, anticipos, CCE ni autorización: los hereda de la original al cancelarse.");
            sustituida = await SustitucionCfdi.CargarSustituibleAsync(_db, sustituyeId, cancellationToken);
        }

        // 2.bis F12-PR1: snapshot del emisor (razón social + LugarExpedicion del
        // master). Falla aquí si la empresa no tiene CP fiscal — sin quemar folio.
        var emisor = await EmisorSnapshot.ResolverAsync(
            _empresasFiscal, empresaId, cancellationToken);

        // 3. F4-PR2: cargar y validar los anticipos a amortizar ANTES de timbrar —
        // la relación 07 va en el XML (inmutable) y un saldo insuficiente debe
        // abortar sin quemar el timbre.
        var anticipos = await CargarYValidarAnticiposAsync(command, receptor.Rfc, cancellationToken);

        // 3.bis B2: si se emite desde un pedido, debe estar Importado (re-facturable).
        // F6.1: se carga ANTES de la autorización de activo — un pedido de venta
        // directa subtipo Activo fuerza el comportamiento VentaActivoFijo y, con
        // él, la autorización del Director de Administración y Finanzas (CA6.12). Un pedido manual
        // legacy (sin clase) se rechaza aquí.
        var pedido = await CargarYValidarPedidoAsync(command, true, cancellationToken);
        var salida = await CargarYValidarSalidaAsync(command, pedido, cancellationToken);
        // FAC-08: con salida, las partidas de origen son las posiciones de la salida.
        var partidasOrigen = salida is null
            ? pedido?.Lineas.OrderBy(l => l.Posicion).ToArray()
            : salida.Lineas.OrderBy(l => l.Posicion)
                .Select(l => pedido!.Lineas.Single(p => p.Posicion == l.Posicion)).ToArray();

        // 3.ter F9: venta de activo fijo exige autorización del Director de Administración y Finanzas
        // (no se timbra sin ella). Se valida antes de reservar folio.
        var autorizacion = await CargarYValidarAutorizacionAsync(command, cancellationToken);

        if (copiaCe is not null && pedido is not null)
        {
            // FAC-08: con salida (un contenedor), el 100 % se exige a la salida, no al pedido;
            // así no se facturan las piezas del pedido completo en cada contenedor.
            var origen = partidasOrigen!;
            var cantidades = salida?.Lineas.OrderBy(l => l.Posicion).Select(l => l.Cantidad).ToArray()
                ?? origen.Select(l => l.Cantidad).ToArray();
            if (origen.Length != command.Lineas.Count || origen.Where((l, i) => cantidades[i] != command.Lineas[i].Cantidad || l.ProductoId != command.Lineas[i].ProductoId).Any())
                throw new BusinessRuleException("FAC_PEDIDO_NO_EMBARCADO", salida is null
                    ? "La factura debe incluir todas las posiciones y cantidades del pedido ligado al expediente (D19)."
                    : "La factura debe incluir todas las posiciones y cantidades de la salida (FAC-08).");
        }

        if (copiaCe is not null && pedido is not null && _productos is not null)
        {
            var lineasPedido = new List<LineaFiscalAValidar>();
            foreach (var linea in partidasOrigen!.Where(l => l.ProductoId.HasValue))
            {
                var maestro = await _productos.ObtenerAsync(linea.ProductoId!.Value, cancellationToken);
                lineasPedido.Add(new LineaFiscalAValidar(linea.Posicion, linea.ProductoId, linea.ProductoDescripcion,
                    maestro?.ClaveProdServSat, maestro?.ClaveUnidadSat, maestro?.ObjetoImp, maestro?.TasaIvaTraslado));
            }
            await ValidadorProductoFiscal.ValidarAsync(lineasPedido, _productos, cancellationToken);
        }
        if (pedido is not null && command.ClienteId is { } clienteId && clienteId != pedido.ClienteId)
            throw new ReceptorFiscalInvalidoException(
                [new("clienteId", "El cliente seleccionado no corresponde al cliente del pedido; recarga el pedido.")], pedido.ClienteId);

        // 86e3zg0vb: el anticipo se cruza por cliente, no solo por RFC — con XEXX todos los extranjeros comparten RFC.
        AnticiposDelCliente.Verificar(anticipos.Select(a => a.Anticipo), command.ClienteId ?? pedido?.ClienteId, receptor.Rfc);

        await _receptorFiscal.ValidarAsync(receptor, emisor, command.ClienteId ?? pedido?.ClienteId,
            cancellationToken, exportacionConCce: command.ComportamientoFiscal == ComportamientoFiscal.ExportacionConCce,
            numRegIdTrib: command.Cce?.ReceptorNumRegIdTrib, paisResidencia: command.Cce?.ReceptorPaisResidencia, ventaNueva: true);

        // 4. Reserva atómica de folio vía Compartido.Series. F6.1: la venta
        // directa / emergencia (pedido manual clasificado) usa la serie
        // VentaDirecta de la sucursal (sin caer a la global); el resto, Cfdi.
        // Las reservas de NC (amortización, ranura) no cambian.
        // Prueba S3-4: la exportación con CCE usa la serie de exportación (FEXP)
        // aunque la sucursal tenga su serie CFDI; sin ella, la reserva cae a Cfdi.
        var tipoSerie = TipoSerieFactura(pedido?.EsVentaDirectaManual == true, command.ComportamientoFiscal);
        var reserva = await _sender.Send(
            new ReservarFolioCommand(
                empresaId,
                command.SucursalId,
                tipoSerie,
                Millet.Facturacion.Application.Timbrado.CfdiEmisionBuilder.DiaFiscal(ahora)),
            cancellationToken);
        // B2: desde aquí el folio ya está consumido; si la emisión aborta, queda Descartada.
        _folioReservado = reserva;
        var commandReservado = command;
        _armarFactura = () => FacturaVentaArmado.Construir(
            commandReservado, empresaId, reserva.Folio, reserva.Numero, _user.UserId,
            receptor, emisor, anio, mes);

        var partidasPedido = partidasOrigen;
        var lineasImpresion = new List<EmitirFacturaVentaLinea>();
        for (var indice = 0; indice < command.Lineas.Count; indice++)
        {
            var linea = command.Lineas[indice];
            // La posición y el producto identifican el origen incluso en una factura parcial.
            var origen = partidasPedido is not null && indice < partidasPedido.Length
                && partidasPedido[indice].ProductoId == linea.ProductoId ? partidasPedido[indice] : null;
            var proporcion = origen is { Cantidad: > 0 } ? linea.Cantidad / origen.Cantidad : 0m;
            var peso = linea.PesoKg ?? origen?.PesoKg * proporcion;
            if (peso is null && linea.ProductoId is Guid productoId && _productos is not null)
                peso = (await _productos.ObtenerAsync(productoId, cancellationToken))?.PesoUnitarioKg * linea.Cantidad;
            lineasImpresion.Add(linea with {
                Referencia = linea.Referencia ?? origen?.Referencia,
                PesoKg = peso, AreaPie2 = linea.AreaPie2 ?? origen?.AreaPie2 * proporcion
            });
        }
        command = command with { Lineas = lineasImpresion };

        // 5. Construir la factura (borrador) + líneas + totales + CCE (5.bis
        // F7-PR1). U1.8a: armado compartido con la proforma del pedido para que
        // importes, impuestos y redondeos sean idénticos.
        var factura = FacturaVentaArmado.Construir(
            command, empresaId, reserva.Folio, reserva.Numero, _user.UserId,
            receptor, emisor, anio, mes);
        _facturaEnCurso = factura;
        // M7: el IVA de la parcialidad se redondea sobre el acumulado de la posición, para que la suma
        // de las facturas de las salidas sea el total del pedido (sin centavo de más).
        if (salida is not null)
            await IvaParcialidades.AjustarIvaAsync(_db, factura, salida, cancellationToken);

        // 86e3tfdw2: observaciones del cajero (solo PDF, nunca XML). Antes de
        // cualquier cambio de estado (retención por pedimento / timbrado).
        var condicionesPago = command.CondicionesPago ?? pedido?.CondicionPagoOrigen;
        // M12: una PPD es pago diferido; «CONTADO» (p. ej. heredado del pedido de A+W)
        // la contradice, así que se descarta y manda el plazo de la línea de crédito.
        if (command.MetodoPago == "PPD" && EsContado(condicionesPago)) condicionesPago = null;
        if (condicionesPago is null && command.MetodoPago == "PPD"
            && (command.ClienteId ?? pedido?.ClienteId) is Guid clienteCreditoId && _plazoCredito is not null)
        {
            // El bloqueo de crédito no cambia el plazo pactado con el cliente.
            var plazo = await _plazoCredito.ObtenerPlazoDiasAsync(clienteCreditoId, command.Moneda, cancellationToken);
            if (plazo > 0) condicionesPago = $"{plazo} DIAS";
        }
        if (condicionesPago is null && command.MetodoPago == "PUE") condicionesPago = "CONTADO";

        factura.EditarDatosCliente(command.OrdenCompraCliente,
            command.ReferenciaCliente ?? pedido?.NumeroPedido,
            condicionesPago,
            command.Proforma, command.Observaciones ?? pedido?.Comentarios);
        if (salida is not null) factura.LigarSalida(salida.Id, salida.PedidoFacturableId);

        if (copiaCe is not null) factura.TomarEmbarqueCe(copiaCe);
        else if (sustituida?.DatosEmbarqueCe is { } copiaOriginal) factura.TomarEmbarqueCe(copiaOriginal with { PedidoFacturableId = sustituida.PedidoFacturableId });

        // 5.quater F9: vincula + consume la autorización de venta de activo fijo.
        if (autorizacion is not null)
        {
            factura.VincularAutorizacion(autorizacion.Id);
            autorizacion.MarcarUsada(factura.Id);
        }

        // 5.bis.b FAC-08b: relación 04 a la original (va en el XML, antes de timbrar o retener).
        if (sustituida is not null)
            factura.AgregarRelacion(sustituida.Uuid!, SustitucionCfdi.TipoRelacion);

        // 5.ter F7-PR2: compuerta de pedimento (§3.bis.5, por factura, nunca
        // global). Si requiere pedimento y aún no lo tiene, se retiene sin timbrar
        // (solo esta factura); AplicarPedimentoCommand la completa después.
        if (factura.RequierePedimento && !factura.PedimentoCompleto)
        {
            factura.MarcarPendientePedimento();
            // B2/B3: el pedido queda tomado por la factura retenida (no re-facturable).
            // FAC-08: con salida, la toma es de la salida (ligada en la factura).
            if (salida is null) pedido?.MarcarFacturado(factura.Id);
            _db.FacturasVenta.Add(factura);
            await _db.SaveChangesAsync(cancellationToken);
            _facturaPersistida = true;
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
        _pacRespondio = true;

        // 7.bis FAC-08b: timbrada la sustituta, se solicita la cancelación de la original
        // (motivo 01, sustituto = UUID nuevo). Queda pendiente de autorización (F6.5).
        await SustitucionCfdi.SolicitarCancelacionOriginalAsync(
            _db, factura, _user.UserId, _user.UserName, ahora, cancellationToken);

        // 8. F4-PR2: las NC de amortización solo se emiten sobre una factura
        // timbrada; si el timbre de la factura falló, se aborta sin guardar nada.
        // B5: el motivo del PAC viaja en el mensaje; sin él, el usuario no sabe qué corregir.
        if (anticipos.Count > 0 && factura.Estado != EstadoTimbrado.Timbrado)
            throw new BusinessRuleException(
                "FACTURA_NO_TIMBRADA_PARA_AMORTIZAR",
                MensajeNoTimbradaParaAmortizar(factura.TimbradoErrorCodigo, factura.TimbradoErrorMensaje));

        // 9. F10-PR1: evento de integración (al Outbox en la misma TX vía el
        // interceptor); Contabilidad arma la póliza desde el evento (C1.5). Solo si quedó Timbrada.
        if (factura.Estado == EstadoTimbrado.Timbrado)
        {
            await _eventos.PublishAsync(EventosContablesFacturacion.FacturaVentaTimbrada(
                factura, ahora, command.ClienteId ?? pedido?.ClienteId,
                await EventosContablesFacturacion.TiposProductoAsync(_productos, factura, cancellationToken), pedido),
                cancellationToken);
        }

        // [Decisión 01-G] G5: el pedido queda tomado por su factura desde el
        // PRIMER intento (1 pedido → 1 documento → N intentos), falle o no el
        // timbre. Un fallo se resuelve SOBRE la factura (reintentar-timbrado /
        // descartar), nunca emitiendo una segunda factura del mismo pedido.
        // FAC-08: una factura de salida no toma el pedido: queda Importado para las demás
        // salidas y la salida queda tomada por esta factura (ligada en SalidaFacturableId).
        // M8: la que completa el 100 % (todas las salidas con factura timbrada) sí lo marca Facturado.
        if (pedido is not null && salida is not null)
            await PedidoPorParcialidades.TomarPedidoSiCompletoAsync(_db, factura, pedido, cancellationToken);
        if (pedido is not null && salida is null)
        {
            pedido.MarcarFacturado(factura.Id);

            // TR-01: el aviso a A+W ya no es un UPDATE por pedido (B6): la factura viaja en
            // FacturaVentaTimbradaIntegrationEvent y Integraciones.Aw deja su fila Documento.
            if (pedido.Origen == OrigenPedido.Aw && factura.Estado == EstadoTimbrado.Timbrado)
            {
                var control = await _db.IngestaControles
                    .FirstOrDefaultAsync(c => c.PedidoFacturableId == pedido.Id, cancellationToken);
                if (control is not null)
                {
                    control.CambiarEstado(EstadoIngesta.Facturado);
                }
            }
        }

        // U-1 (86e3yxn2p): la factura timbrada ya existe ante el SAT; se guarda
        // ANTES de emitir sus notas. Cada nota (amortización, ranura) se guarda
        // en su propia operación y, si no timbra, queda pendiente de timbrar
        // ligada a la factura, con el motivo, para reintentarla desde la bandeja.
        _db.FacturasVenta.Add(factura);
        await _db.SaveChangesAsync(cancellationToken);
        _facturaPersistida = true;

        // 10. F4-PR2: una NC por anticipo (relación 07 a la factura final). 86e3zg0vb: los que se aplican después solo quedan
        // apartados para esta factura (ya relacionada 07); su NC la emite «Aplicar anticipo».
        foreach (var diferido in anticipos.Where(a => a.AplicarDespues))
            diferido.Anticipo.Vincular(factura.Id, diferido.Importe, ahora);
        if (anticipos.Any(a => a.AplicarDespues))
            await _db.SaveChangesAsync(cancellationToken);
        var inmediatos = anticipos.Where(a => !a.AplicarDespues).ToList();
        IReadOnlyList<NotaCreditoAmortizacionEmitida> ncResp = inmediatos.Count > 0
            ? await AmortizarAnticiposAsync(factura, receptor, inmediatos, anio, mes, ahora, cancellationToken)
            : [];

        // 11. RANURA-PR2: NC automática por la ranura del pedido A+W
        // (relación 01) — la factura va por el total y la caja cobra
        // total − NC acreditadas ([Decisión 13-K]). Solo si quedó Timbrada; si
        // el timbre falló, la emite el reintento (ReintentarTimbradoHandler).
        var ncRanura = await NcRanuraEmisor.EmitirSiAplicaAsync(
            _db, _sender, _fiscal, _cfdiRepo, _eventos,
            factura, pedido, _user.UserId, ahora, _receptorFiscal, cancellationToken);
        if (ncRanura is not null)
            await _db.SaveChangesAsync(cancellationToken);

        return new EmitirFacturaVentaResponse(
            Id: factura.Id,
            Estado: factura.Estado.ToString(),
            Uuid: factura.Uuid,
            Folio: factura.Folio,
            Total: factura.Total,
            Version: factura.Version,
            NotasCreditoAmortizacion: ncResp.Count > 0 ? ncResp : null,
            NotaCreditoRanura: ncRanura,
            TimbradoErrorCodigo: factura.TimbradoErrorCodigo,
            TimbradoErrorMensaje: factura.TimbradoErrorMensaje);
    }

    /// <summary>M12: «CONTADO» contradice una PPD (pago diferido).</summary>
    private static bool EsContado(string? condiciones) =>
        string.Equals(condiciones?.Trim(), "CONTADO", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// B2: hay que dejar rastro si el folio se reservó y la factura no llegó a guardarse. Si el PAC
    /// ya respondió timbrado o en proceso, NO se descarta: pudo existir el CFDI ante el SAT.
    /// </summary>
    private bool DebeDejarFolioDescartado() =>
        _folioReservado is not null && !_facturaPersistida
        && !(_pacRespondio && _facturaEnCurso?.Estado is EstadoTimbrado.Timbrado or EstadoTimbrado.TimbradoEnProceso);

    /// <summary>
    /// B2: guarda la factura de la emisión abortada en <c>Descartada</c> con su folio y el motivo
    /// (texto del PAC o de la excepción) y la registra descartada en la bitácora de contingencias.
    /// Se descarta todo lo demás que la emisión había preparado (pedido, salida, anticipos,
    /// autorización): no se afectan saldos ni pedidos. Si esto también falla, se registra en el log
    /// y se propaga la excepción original.
    /// </summary>
    private async Task GuardarFolioDescartadoAsync(Exception ex)
    {
        try
        {
            _db.ChangeTracker.Clear();
            var factura = _facturaEnCurso ?? _armarFactura!();
            var codigo = factura.TimbradoErrorCodigo
                ?? (ex is Millet.SharedKernel.Domain.Exceptions.DomainException d ? d.Code : "EMISION_ABORTADA");
            var motivo = string.IsNullOrWhiteSpace(factura.TimbradoErrorMensaje)
                ? $"Emisión abortada: {ex.Message}"
                : $"Emisión abortada: {ex.Message} Motivo del PAC: {factura.TimbradoErrorMensaje}";
            factura.DescartarPorEmisionAbortada(codigo, motivo);
            _db.FacturasVenta.Add(factura);

            var ahora = _clock.UtcNow;
            var contingencia = ContingenciaTimbrado.Registrar(
                factura.EmpresaId, factura.SucursalId,
                Millet.Facturacion.Application.Timbrado.Contingencias.ContingenciasTimbrado.Clasificar(codigo)
                    ?? TipoContingenciaTimbrado.Otra,
                OrigenContingenciaTimbrado.Automatica, motivo, factura.TimbradoErrorCodigo, ahora,
                factura.Id, factura.Folio, registradaPorId: null, registradaPorNombre: null);
            contingencia.MarcarDescartada(ahora, null);
            _db.ContingenciasTimbrado.Add(contingencia);

            await _db.SaveChangesAsync(CancellationToken.None);
            _logger?.LogWarning(ex, "Emisión abortada después de reservar el folio {Folio}: la factura queda Descartada.", factura.Folio);
        }
        catch (Exception guardado)
        {
            _logger?.LogError(guardado,
                "No se pudo guardar como Descartada la factura del folio {Folio} tras la emisión abortada.",
                _folioReservado?.Folio);
        }
    }

    /// <summary>
    /// B5: mensaje del rechazo de una factura con anticipos que no quedó timbrada. Lleva el
    /// código y el motivo del PAC para que se corrija el dato antes de volver a emitir.
    /// </summary>
    /// <summary>Serie con la que se folia la factura de venta (F6.1 y prueba S3-4).</summary>
    internal static TipoDocumentoSerie TipoSerieFactura(bool esVentaDirectaManual, ComportamientoFiscal comportamiento) =>
        esVentaDirectaManual ? TipoDocumentoSerie.VentaDirecta
        : comportamiento == ComportamientoFiscal.ExportacionConCce ? TipoDocumentoSerie.Exportacion
        : TipoDocumentoSerie.Cfdi;

    internal static string MensajeNoTimbradaParaAmortizar(string? codigo, string? motivo)
    {
        const string Base = "No se pueden amortizar anticipos: la factura no quedó timbrada.";
        if (string.IsNullOrWhiteSpace(motivo))
            return string.IsNullOrWhiteSpace(codigo) ? Base : $"{Base} Código del PAC: {codigo}.";
        return string.IsNullOrWhiteSpace(codigo) || motivo.Contains(codigo, StringComparison.OrdinalIgnoreCase)
            ? $"{Base} Motivo del PAC: {motivo}"
            : $"{Base} Motivo del PAC ({codigo}): {motivo}";
    }

    /// <summary>
    /// Carga los anticipos a amortizar con su CFDI y valida (antes de timbrar):
    /// existencia, estado Abierto, mismo cliente (RFC receptor) e importe ≤ saldo
    /// disponible. Un fallo aquí aborta sin quemar el timbre de la factura.
    /// </summary>
    /// <summary>
    /// Si el comportamiento es <c>VentaActivoFijo</c>, exige y valida la
    /// autorización del Director de Administración y Finanzas (existente y aún disponible). Para
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
                "La venta de un activo fijo requiere la autorización del Director de Administración y Finanzas antes de timbrar.");

        var autorizacion = await _db.AutorizacionesVentaActivo
            .FirstOrDefaultAsync(a => a.Id == autorizacionId, cancellationToken)
            ?? throw new EntityNotFoundException("AUTORIZACION_NO_ENCONTRADA", $"No existe la autorización {autorizacionId}.");

        if (autorizacion.Estado != EstadoAutorizacionActivo.Autorizada)
            throw new BusinessRuleException(
                "AUTORIZACION_NO_DISPONIBLE",
                $"La autorización ya fue {autorizacion.Estado} y no puede reutilizarse.");

        autorizacion.ValidarRegistrador(_user.UserId);
        return autorizacion;
    }

    /// <summary>
    /// FAC-08: con <c>SalidaFacturableId</c>, carga la salida del pedido y exige que no tenga
    /// otra factura vigente (una cancelada o descartada la libera) y que las líneas sean las de
    /// la salida. Sin salida, un pedido que ya tiene salidas no se factura completo.
    /// </summary>
    private async Task<SalidaFacturable?> CargarYValidarSalidaAsync(
        EmitirFacturaVentaCommand command, PedidoFacturable? pedido, CancellationToken cancellationToken)
    {
        if (command.SalidaFacturableId is not Guid salidaId)
        {
            if (pedido is not null && await _db.SalidasFacturables.AnyAsync(s => s.PedidoFacturableId == pedido.Id && s.DescartadaEn == null, cancellationToken))
                throw new BusinessRuleException("PEDIDO_CON_SALIDAS",
                    "El pedido ya se factura por salidas parciales; factura la salida pendiente.");
            return null;
        }
        if (pedido is null)
            throw new BusinessRuleException("FAC_SALIDA_SIN_PEDIDO", "Para facturar una salida indica también su pedido.");

        var salida = await _db.SalidasFacturables.Include(s => s.Lineas)
            .FirstOrDefaultAsync(s => s.Id == salidaId && s.PedidoFacturableId == pedido.Id, cancellationToken)
            ?? throw new EntityNotFoundException("SALIDA_NO_ENCONTRADA", $"El pedido no tiene la salida {salidaId}.");
        if (salida.Descartada)
            throw new BusinessRuleException("SALIDA_DESCARTADA",
                $"La parcialidad {salida.NumeroParcialidad} se descartó ({salida.MotivoDescarte}); no se factura.");

        if (await _db.FacturasVenta.AnyAsync(f => f.SalidaFacturableId == salidaId
                && f.Estado != EstadoTimbrado.Cancelado && f.Estado != EstadoTimbrado.Descartada, cancellationToken))
            throw new BusinessRuleException("SALIDA_YA_FACTURADA",
                $"La parcialidad {salida.NumeroParcialidad} ya tiene una factura vigente.");

        var lineas = salida.Lineas.OrderBy(l => l.Posicion).ToArray();
        if (lineas.Length != command.Lineas.Count || lineas.Where((l, i) => l.Cantidad != command.Lineas[i].Cantidad
                || pedido.Lineas.Single(p => p.Posicion == l.Posicion).ProductoId != command.Lineas[i].ProductoId).Any())
            throw new BusinessRuleException("FAC_SALIDA_LINEAS_DISTINTAS",
                "La factura debe llevar las posiciones y cantidades de la salida, en orden de posición.");
        return salida;
    }

    /// <summary>
    /// Si la emisión es desde un pedido (B2), lo carga y exige que esté
    /// <c>Importado</c> (no Bloqueado/Facturado/Cancelado). Devuelve la entidad
    /// trackeada para marcarla <c>Facturado</c> al emitir; <c>null</c> si no hay
    /// pedido de origen.
    /// </summary>
    private async Task<PedidoFacturable?> CargarYValidarPedidoAsync(
        EmitirFacturaVentaCommand command, bool cargarLineas, CancellationToken cancellationToken)
    {
        if (command.PedidoFacturableId is not Guid pedidoId)
            return null;

        // PLATFORM-TODO(<U1.6LineasPedido>): la validación U1.6 de líneas del pedido no corre para pedidos no ligados; decidir con Uziel
        IQueryable<PedidoFacturable> query = _db.PedidosFacturables;
        if (cargarLineas) query = query.Include(p => p.Lineas);
        var pedido = await query.FirstOrDefaultAsync(p => p.Id == pedidoId, cancellationToken)
            ?? throw new EntityNotFoundException("PEDIDO_NO_ENCONTRADO", $"No existe el pedido facturable {pedidoId}.");

        if (pedido.Estado != EstadoPedidoFacturable.Importado)
            throw new BusinessRuleException(
                "PEDIDO_NO_FACTURABLE",
                $"El pedido {pedidoId} no es facturable (estado actual: {pedido.Estado}; sólo Importado).");

        // F6.1: un pedido manual sin clase (legacy) no se factura hasta clasificarlo.
        pedido.AsegurarClasificadoParaFacturar();

        // F6.1 / CA6.12: la venta directa de un activo va siempre como
        // VentaActivoFijo, para que la autorización del Director de Administración y Finanzas no
        // pueda saltarse cambiando el comportamiento en la emisión.
        if (pedido.EsVentaDirectaManual
            && pedido.SubtipoVentaDirecta == SubtipoVentaDirecta.Activo
            && command.ComportamientoFiscal != ComportamientoFiscal.VentaActivoFijo)
            throw new BusinessRuleException(
                "FAC_PEDIDO_ACTIVO_COMPORTAMIENTO",
                "El pedido es una venta directa de activo fijo: emítelo con el comportamiento «Venta de activo fijo» y la autorización del Director de Administración y Finanzas.");

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

            resultado.Add(new AnticipoAmortizable(anticipo, facturaAnticipo, sol.Importe, sol.ComentarioNotaCredito, sol.AplicarDespues));
        }

        return resultado;
    }

    /// <summary>
    /// Por cada anticipo: reserva folio de NC, autogenera la NC de amortización
    /// (relación 07 a la factura de anticipo y a la factura final), la timbra
    /// y reduce el saldo del anticipo. U-1: cada NC se guarda en su propia
    /// operación; si no queda Timbrada, queda pendiente de timbrar. Tasa de IVA estándar para el desglose; el detalle fiscal
    /// exacto lo afina F12.
    /// </summary>
    private async Task<IReadOnlyList<NotaCreditoAmortizacionEmitida>> AmortizarAnticiposAsync(
        FacturaVenta factura,
        DatosFiscalesReceptor receptor,
        IReadOnlyList<AnticipoAmortizable> anticipos,
        int anio, int mes, DateTimeOffset ahora,
        CancellationToken cancellationToken)
    {
        var emitidas = new List<NotaCreditoAmortizacionEmitida>();

        foreach (var a in anticipos)
        {
            var reservaNc = await _sender.Send(
                new ReservarFolioCommand(
                    factura.EmpresaId, factura.SucursalId,
                    TipoDocumentoSerie.NotaCredito, Millet.Facturacion.Application.Timbrado.CfdiEmisionBuilder.DiaFiscal(ahora)),
                cancellationToken);

            var nc = NotaCredito.CrearAmortizacion(
                empresaId: factura.EmpresaId,
                folio: reservaNc.Folio,
                folioNumero: reservaNc.Numero,
                sucursalId: factura.SucursalId,
                cajaId: factura.CajaId,
                usuarioEmisorId: _user.UserId,
                // 86e3zg0vb: forma 30 (aplicación de anticipos) y uso G02, como las NC de aplicación de Millet.
                receptor: NotaCredito.ReceptorAplicacion(receptor),
                emisor: factura.SnapshotEmisor(),
                formaPago: NotaCredito.FormaPagoAplicacionAnticipo,
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
                // 86e3zg0vb: la tasa del anticipo (16 % nacional, 0 % exportación), ya no 16 % fija.
                tasaIva: a.FacturaAnticipo.TasaIvaParaAplicacion());
            nc.EstablecerNumRegIdTribReceptor(factura.ComplementoCce?.ReceptorNumRegIdTrib ?? a.FacturaAnticipo.ReceptorNumRegIdTrib);

            // Millet (4-nov): la NC de aplicación relaciona solo la factura de venta (el anticipo va en la factura).
            if (!string.IsNullOrWhiteSpace(factura.Uuid))
                nc.AgregarRelacion(factura.Uuid!, "07");
            // FMT-6 (86e3yxe8f): comentario libre que captura el usuario para esta NC.
            nc.FijarComentarios(a.ComentarioNotaCredito);

            var timbre = await TimbradoEjecutor.TimbrarYAplicarAsync(_db, 
                nc, CfdiEmisionBuilder.DesdeNotaCredito(nc, ahora),
                _fiscal, _cfdiRepo, ahora, cancellationToken);

            _db.NotasCredito.Add(nc);
            if (timbre.Estado == TimbradoEstado.Timbrado && !string.IsNullOrWhiteSpace(timbre.Uuid))
            {
                a.Anticipo.RegistrarAmortizacion(factura.Id, nc.Id, a.Importe, ahora);

                // F10-PR1: evento de NC de amortización timbrada.
                await _eventos.PublishAsync(EventosContablesFacturacion.NotaCreditoTimbrada(
                    nc, ahora, factura.Id, a.Anticipo.Id, a.Anticipo.ClienteId),
                    cancellationToken);
            }
            else
            {
                // U-1: la NC queda pendiente de timbrar. El importe queda comprometido
                // con esta factura (no se usa en otra) sin reducir el saldo; el
                // reintento timbrado lo amortiza y descartarla lo libera.
                a.Anticipo.Vincular(factura.Id, a.Importe, ahora);
            }
            await _db.SaveChangesAsync(cancellationToken);

            emitidas.Add(new NotaCreditoAmortizacionEmitida(
                Id: nc.Id, AnticipoId: a.Anticipo.Id, Folio: nc.Folio, Uuid: nc.Uuid,
                Total: nc.Total, SaldoAnticipoRestante: a.Anticipo.Saldo,
                Estado: nc.Estado.ToString(), TimbradoErrorCodigo: nc.TimbradoErrorCodigo,
                TimbradoErrorMensaje: nc.TimbradoErrorMensaje));
        }

        return emitidas;
    }

    /// <summary>Anticipo cargado para amortización: su saldo, su CFDI (para el UUID) y el importe a aplicar.</summary>
    private sealed record AnticipoAmortizable(
        Millet.Facturacion.Domain.Anticipos.Anticipo Anticipo,
        Millet.Facturacion.Domain.Anticipos.FacturaAnticipo FacturaAnticipo,
        decimal Importe,
        string? ComentarioNotaCredito,
        bool AplicarDespues = false);

    private async Task ValidarCatalogosSatAsync(
        EmitirFacturaVentaCommand command,
        CancellationToken cancellationToken)
    {
        if (!await _catalogos.ExisteMonedaAsync(command.Moneda, cancellationToken))
            throw new BusinessRuleException("MONEDA_INVALIDA", $"La moneda '{command.Moneda}' no existe en el catálogo SAT.");
        if (!await _catalogos.ExisteFormaPagoAsync(command.FormaPago, cancellationToken))
            throw new BusinessRuleException("FORMA_PAGO_INVALIDA", $"La forma de pago '{command.FormaPago}' no existe o está desactivada en el catálogo SAT. Selecciona una forma de pago activa.");
        if (!await _catalogos.ExisteRegimenFiscalAsync(command.RegimenFiscalEmisor, cancellationToken))
            throw new BusinessRuleException("REGIMEN_EMISOR_INVALIDO", $"El régimen fiscal del emisor '{command.RegimenFiscalEmisor}' no existe en el catálogo SAT.");
    }

}
