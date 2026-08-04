using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.FacturaProveedor;

/// <summary>
/// Agregado raíz central del módulo CxP (§4.2 del 00-levantamiento).
/// Representa el pasivo con el proveedor; concentra estado, saldo,
/// evidencias, conciliación con OC, snapshot de tolerancia.
///
/// <para>
/// **F3-PR1 alcance:** captura con OC + conciliación + cancelación +
/// edición pre-autorización. Las operaciones de aplicación de
/// <c>NotaCreditoProveedor</c> / <c>AnticipoProveedor</c> entran en F6
/// (sobrescribirán <see cref="NcAplicadasTotal"/> /
/// <see cref="AnticipoAplicadoTotal"/>). Eventos del Outbox los publica
/// F3-PR2.
/// </para>
///
/// <para>
/// Multi-tenant (<see cref="IPerteneceAEmpresa"/>) + fiscalmente
/// relevante (<see cref="IFiscalmenteRelevante"/>): el query filter del
/// BaseDbContext aplica empresa + soft-delete automáticamente.
/// </para>
/// </summary>
public sealed class FacturaProveedor : BaseEntity, IPerteneceAEmpresa, IFiscalmenteRelevante
{
    public Guid EmpresaId { get; set; }

    /// <summary>FK opcional al CFDI origen — null si es factura interna sin CFDI.</summary>
    public Guid? CfdiRecibidoId { get; private set; }

    /// <summary>UUID fiscal heredado del CFDI; null si es factura interna.</summary>
    public string? UuidCfdi { get; private set; }

    public Guid ProveedorId { get; private set; }
    public Guid SucursalId { get; private set; }

    /// <summary>Folio + serie del proveedor (snapshot del CFDI o capturado).</summary>
    public string? FolioProveedor { get; private set; }
    public string? SerieProveedor { get; private set; }

    public DateTimeOffset FechaDocumento { get; private set; }
    public DateTimeOffset FechaContabilizacion { get; private set; }
    public DateOnly FechaVencimiento { get; private set; }

    public string Moneda { get; private set; } = "MXN";
    public decimal? TipoCambio { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal Descuentos { get; private set; }
    public decimal ImpuestosTrasladados { get; private set; }
    public decimal Retenciones { get; private set; }
    public decimal Total { get; private set; }

    /// <summary>OC asociada (opcional — null si es factura directa).</summary>
    public Guid? OrdenCompraId { get; private set; }

    /// <summary>Encargado de compras snapshot desde la OC al momento de captura.</summary>
    public Guid? EncargadoComprasSnapshot { get; private set; }

    public EstadoPasivo Estado { get; private set; }

    /// <summary>
    /// Snapshot de tolerancia del proveedor al momento de captura
    /// (§3.bis.3). NULL si no aplica conciliación con OC.
    /// </summary>
    public ToleranciaTipo? ToleranciaTipo { get; private set; }
    public decimal? ToleranciaValor { get; private set; }

    /// <summary>
    /// Diferencia absoluta entre total de factura y total de OC al
    /// momento de captura (snapshot). 0 si no aplica.
    /// </summary>
    public decimal DiferenciaContraOc { get; private set; }

    /// <summary>Redondeo aplicado dentro de tolerancia (para que el saldo sea exacto).</summary>
    public decimal RedondeoAplicado { get; private set; }

    // ---- Aplicaciones acumuladas (F6+) ----
    public decimal AnticipoAplicadoTotal { get; private set; }
    public decimal NcAplicadasTotal { get; private set; }
    public decimal ImportePagado { get; private set; }

    /// <summary>Saldo pendiente = Total − Anticipos − NCs − Pagado. Calculado, persistido.</summary>
    public decimal SaldoPendiente => Total - AnticipoAplicadoTotal - NcAplicadasTotal - ImportePagado;

    /// <summary>
    /// F9-PR1: indica si Tesorería confirmó que el complemento de pago
    /// (REPP) del proveedor fue recibido. Bandera operativa para que el
    /// motivo de revisión "Falta complemento de pago" pueda liberarse.
    /// </summary>
    public bool ReppRecibido { get; private set; }

    /// <summary>
    /// MetodoPago SAT (PUE/PPD) del CFDI de la factura (TES-PR8, [T-G11]).
    /// Se copia del <c>CfdiRecibido</c> ligado en la captura y viaja a
    /// Tesorería en <c>pasivo.autorizado-para-pago.v1</c> — solo PPD exige
    /// REPP del proveedor. Nullable: facturas sin CFDI ligado o previas a
    /// esta columna.
    /// </summary>
    public string? MetodoPago { get; private set; }

    /// <summary>Asigna el MetodoPago derivado del CFDI ligado (TES-PR8). No-op con null/vacío.</summary>
    public void AsignarMetodoPago(string? metodoPago)
    {
        var v = metodoPago?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(v)) MetodoPago = v;
    }

    // ---- Cancelación ----
    public MotivoCancelacion? MotivoDeCancelacion { get; private set; }
    public string? MotivoCancelacionTexto { get; private set; }
    public DateTimeOffset? FechaCancelacion { get; private set; }

    // ---- Revisión (placeholder F4) ----
    public bool EnRevision { get; private set; }
    public Guid? MotivoRevisionId { get; private set; }
    public Guid? DependenciaRevisoraId { get; private set; }
    public DateTimeOffset? FechaEntradaRevision { get; private set; }

    private readonly List<LineaFacturaProveedor> _lineas = [];
    public IReadOnlyCollection<LineaFacturaProveedor> Lineas => _lineas.AsReadOnly();

    private readonly List<BitacoraEstadoFactura> _bitacora = [];
    public IReadOnlyCollection<BitacoraEstadoFactura> Bitacora => _bitacora.AsReadOnly();

    private FacturaProveedor() { }

    private FacturaProveedor(
        Guid id,
        Guid empresaId,
        Guid? cfdiRecibidoId,
        string? uuidCfdi,
        Guid proveedorId,
        Guid sucursalId,
        string? folioProveedor,
        string? serieProveedor,
        DateTimeOffset fechaDocumento,
        DateTimeOffset fechaContabilizacion,
        DateOnly fechaVencimiento,
        string moneda,
        decimal? tipoCambio,
        decimal subtotal,
        decimal descuentos,
        decimal impuestosTrasladados,
        decimal retenciones,
        decimal total,
        Guid? ordenCompraId,
        Guid? encargadoComprasSnapshot,
        Tolerancia? tolerancia,
        decimal diferenciaContraOc,
        decimal redondeoAplicado,
        EstadoPasivo estadoInicial) : base(id)
    {
        EmpresaId = empresaId;
        CfdiRecibidoId = cfdiRecibidoId;
        UuidCfdi = uuidCfdi;
        ProveedorId = proveedorId;
        SucursalId = sucursalId;
        FolioProveedor = folioProveedor;
        SerieProveedor = serieProveedor;
        FechaDocumento = fechaDocumento;
        FechaContabilizacion = fechaContabilizacion;
        FechaVencimiento = fechaVencimiento;
        Moneda = moneda;
        TipoCambio = tipoCambio;
        Subtotal = subtotal;
        Descuentos = descuentos;
        ImpuestosTrasladados = impuestosTrasladados;
        Retenciones = retenciones;
        Total = total;
        OrdenCompraId = ordenCompraId;
        EncargadoComprasSnapshot = encargadoComprasSnapshot;
        ToleranciaTipo = tolerancia?.Tipo;
        ToleranciaValor = tolerancia?.Valor;
        DiferenciaContraOc = diferenciaContraOc;
        RedondeoAplicado = redondeoAplicado;
        Estado = estadoInicial;
    }

    /// <summary>
    /// Factory para capturar una factura conciliada con una OC.
    /// El handler resuelve OC + tolerancia y delega los cálculos antes
    /// de invocar este factory. La factura nace en
    /// <see cref="EstadoPasivo.Capturada"/>; el flujo de revisión /
    /// autorización vive en F4.
    /// </summary>
    public static FacturaProveedor CapturarConOc(
        Guid empresaId,
        Guid? cfdiRecibidoId,
        string? uuidCfdi,
        Guid proveedorId,
        Guid sucursalId,
        string? folioProveedor,
        string? serieProveedor,
        DateTimeOffset fechaDocumento,
        DateTimeOffset fechaContabilizacion,
        DateOnly fechaVencimiento,
        string moneda,
        decimal? tipoCambio,
        decimal subtotal,
        decimal descuentos,
        decimal impuestosTrasladados,
        decimal retenciones,
        decimal total,
        Guid ordenCompraId,
        Guid? encargadoComprasSnapshot,
        Tolerancia tolerancia,
        decimal diferenciaContraOc,
        decimal redondeoAplicado,
        DateTimeOffset ahora)
    {
        if (total <= 0)
            throw new BusinessRuleException("FACTURA_TOTAL_INVALIDO", "El total de la factura debe ser > 0.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("FACTURA_MONEDA_INVALIDA", "La moneda debe ser código ISO 4217 de 3 letras.");
        if (fechaVencimiento < DateOnly.FromDateTime(fechaDocumento.UtcDateTime))
            throw new BusinessRuleException(
                "FACTURA_VENCIMIENTO_INVALIDO",
                "La fecha de vencimiento no puede ser anterior a la fecha del documento.");

        var factura = new FacturaProveedor(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            cfdiRecibidoId: cfdiRecibidoId,
            uuidCfdi: uuidCfdi,
            proveedorId: proveedorId,
            sucursalId: sucursalId,
            folioProveedor: folioProveedor,
            serieProveedor: serieProveedor,
            fechaDocumento: fechaDocumento,
            fechaContabilizacion: fechaContabilizacion,
            fechaVencimiento: fechaVencimiento,
            moneda: moneda.ToUpperInvariant(),
            tipoCambio: tipoCambio,
            subtotal: subtotal,
            descuentos: descuentos,
            impuestosTrasladados: impuestosTrasladados,
            retenciones: retenciones,
            total: total,
            ordenCompraId: ordenCompraId,
            encargadoComprasSnapshot: encargadoComprasSnapshot,
            tolerancia: tolerancia,
            diferenciaContraOc: Math.Abs(diferenciaContraOc),
            redondeoAplicado: redondeoAplicado,
            estadoInicial: EstadoPasivo.Capturada);

        factura.RegistrarTransicion(EstadoPasivo.Capturada, EstadoPasivo.Capturada, ahora,
            $"Factura capturada con OC {ordenCompraId}", usuarioId: null);

        return factura;
    }

    /// <summary>
    /// Factory para capturar una factura SIN OC (variantes §7.4 del
    /// 00-levantamiento: Caja Chica, Viáticos, TC Empresarial, Otros
    /// gastos sin OC). La autorización vive en el flujo de
    /// <c>ComprobacionGastos</c> (F7-PR1+); aquí la factura nace en
    /// <see cref="EstadoPasivo.Capturada"/> sin conciliación con OC y
    /// sin tolerancia.
    /// </summary>
    public static FacturaProveedor CapturarSinOc(
        Guid empresaId,
        Guid? cfdiRecibidoId,
        string? uuidCfdi,
        Guid proveedorId,
        Guid sucursalId,
        string? folioProveedor,
        string? serieProveedor,
        DateTimeOffset fechaDocumento,
        DateTimeOffset fechaContabilizacion,
        DateOnly fechaVencimiento,
        string moneda,
        decimal? tipoCambio,
        decimal subtotal,
        decimal descuentos,
        decimal impuestosTrasladados,
        decimal retenciones,
        decimal total,
        string motivoCaptura,
        DateTimeOffset ahora)
    {
        if (total <= 0)
            throw new BusinessRuleException("FACTURA_TOTAL_INVALIDO", "El total de la factura debe ser > 0.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("FACTURA_MONEDA_INVALIDA", "La moneda debe ser código ISO 4217 de 3 letras.");
        if (fechaVencimiento < DateOnly.FromDateTime(fechaDocumento.UtcDateTime))
            throw new BusinessRuleException(
                "FACTURA_VENCIMIENTO_INVALIDO",
                "La fecha de vencimiento no puede ser anterior a la fecha del documento.");

        var factura = new FacturaProveedor(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            cfdiRecibidoId: cfdiRecibidoId,
            uuidCfdi: uuidCfdi,
            proveedorId: proveedorId,
            sucursalId: sucursalId,
            folioProveedor: folioProveedor,
            serieProveedor: serieProveedor,
            fechaDocumento: fechaDocumento,
            fechaContabilizacion: fechaContabilizacion,
            fechaVencimiento: fechaVencimiento,
            moneda: moneda.ToUpperInvariant(),
            tipoCambio: tipoCambio,
            subtotal: subtotal,
            descuentos: descuentos,
            impuestosTrasladados: impuestosTrasladados,
            retenciones: retenciones,
            total: total,
            ordenCompraId: null,
            encargadoComprasSnapshot: null,
            tolerancia: null,
            diferenciaContraOc: 0m,
            redondeoAplicado: 0m,
            estadoInicial: EstadoPasivo.Capturada);

        factura.RegistrarTransicion(EstadoPasivo.Capturada, EstadoPasivo.Capturada, ahora,
            motivoCaptura, usuarioId: null);

        return factura;
    }

    /// <summary>
    /// Agrega una línea al agregado. Se invoca durante captura, antes
    /// de persistir. Una vez la factura sale a Autorizada / Pagada /
    /// Cancelada, no se pueden agregar líneas (§A7 — inmutabilidad
    /// post-autorización).
    /// </summary>
    public LineaFacturaProveedor AgregarLinea(
        Guid? articuloId,
        string? claveProdServ,
        string descripcion,
        decimal cantidad,
        string claveUnidad,
        string? unidad,
        decimal precioUnitario,
        decimal importe,
        decimal? descuento,
        Guid? lineaOcId,
        Guid? conceptoContableId)
    {
        AsegurarMutable("agregar línea");

        var linea = new LineaFacturaProveedor(
            id: Guid.CreateVersion7(),
            facturaProveedorId: Id,
            posicion: _lineas.Count + 1,
            articuloId: articuloId,
            claveProdServ: claveProdServ,
            descripcion: descripcion,
            cantidad: cantidad,
            claveUnidad: claveUnidad,
            unidad: unidad,
            precioUnitario: precioUnitario,
            importe: importe,
            descuento: descuento,
            lineaOcId: lineaOcId,
            conceptoContableId: conceptoContableId);

        _lineas.Add(linea);
        return linea;
    }

    /// <summary>
    /// Cancela la factura con motivo (§A7). Solo aplica desde
    /// <see cref="EstadoPasivo.Capturada"/> o
    /// <see cref="EstadoPasivo.EnRevision"/>. Después de Autorizada se
    /// requiere flujo separado (Tesorería) que en F3-PR1 no aplica.
    /// </summary>
    public void Cancelar(
        MotivoCancelacion motivo,
        string? texto,
        Guid? usuarioId,
        DateTimeOffset ahora)
    {
        if (Estado == EstadoPasivo.Cancelada)
        {
            throw new BusinessRuleException(
                "FACTURA_YA_CANCELADA",
                "La factura ya está cancelada.");
        }
        if (Estado == EstadoPasivo.Pagada)
        {
            throw new BusinessRuleException(
                "FACTURA_PAGADA_NO_CANCELABLE",
                "Una factura pagada no puede cancelarse en este flujo. Coordinar con Tesorería.");
        }
        if (Estado == EstadoPasivo.Autorizada)
        {
            throw new BusinessRuleException(
                "FACTURA_AUTORIZADA_NO_CANCELABLE",
                "Una factura autorizada no puede cancelarse en este flujo. F3-PR1 permite cancelar solo en Capturada/EnRevision (§A7).");
        }
        if (motivo == MotivoCancelacion.OtroConTexto && string.IsNullOrWhiteSpace(texto))
        {
            throw new BusinessRuleException(
                "FACTURA_MOTIVO_TEXTO_REQUERIDO",
                "El motivo 'OtroConTexto' requiere un texto descriptivo.");
        }

        var anterior = Estado;
        Estado = EstadoPasivo.Cancelada;
        MotivoDeCancelacion = motivo;
        MotivoCancelacionTexto = texto;
        FechaCancelacion = ahora;
        EnRevision = false;

        RegistrarTransicion(anterior, EstadoPasivo.Cancelada, ahora, motivo.ToString(), usuarioId);
    }

    /// <summary>
    /// Envía la factura a revisión por una dependencia (§6 del
    /// 00-levantamiento, §A22 del 01-diseno). Aplica solo desde
    /// <see cref="EstadoPasivo.Capturada"/> o
    /// <see cref="EstadoPasivo.Autorizada"/> (re-abrir es posible si
    /// el área detecta algo después). No aplica desde Pagada / Cancelada.
    /// </summary>
    public void EnviarARevision(
        Guid motivoRevisionId,
        Guid dependenciaRevisoraId,
        Guid? usuarioId,
        DateTimeOffset ahora)
    {
        if (Estado is EstadoPasivo.Pagada or EstadoPasivo.Cancelada)
        {
            throw new BusinessRuleException(
                "FACTURA_NO_ENVIABLE_A_REVISION",
                $"Una factura en estado {Estado} no puede enviarse a revisión.");
        }

        var anterior = Estado;
        Estado = EstadoPasivo.EnRevision;
        EnRevision = true;
        MotivoRevisionId = motivoRevisionId;
        DependenciaRevisoraId = dependenciaRevisoraId;
        FechaEntradaRevision = ahora;

        RegistrarTransicion(anterior, EstadoPasivo.EnRevision, ahora,
            $"Enviada a revisión motivo={motivoRevisionId} dep={dependenciaRevisoraId}", usuarioId);
    }

    /// <summary>
    /// Libera la factura desde revisión. Pasa a
    /// <see cref="EstadoPasivo.Capturada"/> (el flujo decide después si
    /// va a Autorizada). Solo aplica desde
    /// <see cref="EstadoPasivo.EnRevision"/>.
    /// </summary>
    public void LiberarRevision(string accionTomada, Guid? usuarioId, DateTimeOffset ahora)
    {
        if (Estado != EstadoPasivo.EnRevision)
        {
            throw new BusinessRuleException(
                "FACTURA_NO_EN_REVISION",
                $"Solo facturas en EnRevision pueden liberarse (actual: {Estado}).");
        }
        if (string.IsNullOrWhiteSpace(accionTomada))
        {
            throw new BusinessRuleException(
                "FACTURA_ACCION_REVISION_VACIA",
                "La acción tomada es obligatoria al liberar revisión.");
        }

        var anterior = Estado;
        Estado = EstadoPasivo.Capturada;
        EnRevision = false;
        MotivoRevisionId = null;
        DependenciaRevisoraId = null;
        FechaEntradaRevision = null;

        RegistrarTransicion(anterior, EstadoPasivo.Capturada, ahora,
            $"Liberada de revisión: {accionTomada}", usuarioId);
    }

    /// <summary>
    /// Aplica un monto de NotaCreditoProveedor al saldo de la factura
    /// (F6-PR2). El handler valida que <c>NotaCredito.SaldoPorAplicar</c>
    /// sea suficiente; aquí solo aplicamos al lado factura con
    /// invariante <c>SaldoPendiente >= 0</c>.
    /// </summary>
    public void AplicarNotaCredito(decimal monto)
    {
        if (Estado is EstadoPasivo.Cancelada or EstadoPasivo.Pagada)
            throw new BusinessRuleException(
                "FACTURA_NO_ACEPTA_NC",
                $"Una factura en {Estado} no acepta aplicación de NC.");
        if (monto <= 0)
            throw new BusinessRuleException("APLICACION_MONTO_INVALIDO", "El monto debe ser > 0.");
        if (monto > SaldoPendiente)
            throw new BusinessRuleException(
                "APLICACION_EXCEDE_SALDO",
                $"Aplicar {monto} dejaría el saldo negativo (saldo actual: {SaldoPendiente}).");

        NcAplicadasTotal += monto;
    }

    /// <summary>
    /// Aplica un monto de AnticipoProveedor al saldo de la factura
    /// (F6-PR2). Mismo patrón que <see cref="AplicarNotaCredito"/>.
    /// </summary>
    public void AplicarAnticipo(decimal monto)
    {
        if (Estado is EstadoPasivo.Cancelada or EstadoPasivo.Pagada)
            throw new BusinessRuleException(
                "FACTURA_NO_ACEPTA_ANTICIPO",
                $"Una factura en {Estado} no acepta aplicación de anticipo.");
        if (monto <= 0)
            throw new BusinessRuleException("APLICACION_MONTO_INVALIDO", "El monto debe ser > 0.");
        if (monto > SaldoPendiente)
            throw new BusinessRuleException(
                "APLICACION_EXCEDE_SALDO",
                $"Aplicar {monto} dejaría el saldo negativo (saldo actual: {SaldoPendiente}).");

        AnticipoAplicadoTotal += monto;
    }

    /// <summary>
    /// Registra el pago de la factura (transición a
    /// <see cref="EstadoPasivo.Pagada"/>). Suma al
    /// <see cref="ImportePagado"/> y verifica que el saldo no quede
    /// negativo. F7-PR4 lo invoca al capturar un movimiento de TC con
    /// CFDI (la TC ya pagó al proveedor) — el flujo normal lo dispara
    /// vía evento desde Tesorería en F9-PR1.
    /// </summary>
    public void RegistrarPago(decimal monto, DateTimeOffset ahora, string observacion)
    {
        if (Estado is not EstadoPasivo.Autorizada)
        {
            throw new BusinessRuleException(
                "FACTURA_NO_PAGABLE",
                $"Solo facturas Autorizadas pueden registrar pago (actual: {Estado}).");
        }
        if (monto <= 0)
            throw new BusinessRuleException("PAGO_MONTO_INVALIDO",
                "El monto del pago debe ser > 0.");
        if (monto > SaldoPendiente)
            throw new BusinessRuleException(
                "PAGO_EXCEDE_SALDO",
                $"Pagar {monto} dejaría saldo negativo (saldo actual: {SaldoPendiente}).");

        ImportePagado += monto;
        if (SaldoPendiente == 0m)
        {
            var anterior = Estado;
            Estado = EstadoPasivo.Pagada;
            RegistrarTransicion(anterior, EstadoPasivo.Pagada, ahora, observacion, usuarioId: null);
        }
    }

    /// <summary>
    /// F9-PR1: revierte un pago (parcial o total) — invocado al recibir
    /// <c>PagoFacturaProveedorRevertidoEvent</c> de Tesorería. Resta
    /// del <see cref="ImportePagado"/>. Si la factura estaba <c>Pagada</c>
    /// y el saldo vuelve a ser &gt; 0, regresa a <see cref="EstadoPasivo.Autorizada"/>.
    /// </summary>
    public void RevertirPago(decimal monto, DateTimeOffset ahora, string observacion)
    {
        if (Estado is not (EstadoPasivo.Pagada or EstadoPasivo.Autorizada))
        {
            throw new BusinessRuleException(
                "FACTURA_NO_REVERTIBLE",
                $"Solo facturas Pagada o Autorizada admiten reversa de pago (actual: {Estado}).");
        }
        if (monto <= 0)
            throw new BusinessRuleException("REVERSA_MONTO_INVALIDO",
                "El monto a revertir debe ser > 0.");
        if (monto > ImportePagado)
            throw new BusinessRuleException(
                "REVERSA_EXCEDE_PAGADO",
                $"Revertir {monto} excede el importe pagado ({ImportePagado}).");

        ImportePagado -= monto;

        if (Estado == EstadoPasivo.Pagada && SaldoPendiente > 0)
        {
            var anterior = Estado;
            Estado = EstadoPasivo.Autorizada;
            RegistrarTransicion(anterior, EstadoPasivo.Autorizada, ahora,
                $"Pago revertido: {observacion}", usuarioId: null);
        }
    }

    /// <summary>
    /// F9-PR1: marca que Tesorería confirmó la recepción del complemento
    /// de pago (REPP) del proveedor. Idempotente.
    /// </summary>
    public void MarcarReppRecibido()
    {
        ReppRecibido = true;
    }

    /// <summary>
    /// Marca la factura como autorizada (§A7 — la transición se va a
    /// disparar desde F4 cuando el flujo de revisión cierre; F3-PR1 lo
    /// expone para uso interno y test de transiciones).
    /// </summary>
    public void Autorizar(Guid? usuarioId, DateTimeOffset ahora)
    {
        if (Estado is not (EstadoPasivo.Capturada or EstadoPasivo.EnRevision))
        {
            throw new BusinessRuleException(
                "FACTURA_NO_AUTORIZABLE",
                $"Solo facturas en Capturada o EnRevision pueden autorizarse (actual: {Estado}).");
        }

        var anterior = Estado;
        Estado = EstadoPasivo.Autorizada;
        EnRevision = false;
        MotivoRevisionId = null;
        DependenciaRevisoraId = null;
        FechaEntradaRevision = null;
        RegistrarTransicion(anterior, EstadoPasivo.Autorizada, ahora, "Autorizada", usuarioId);
    }

    /// <summary>
    /// Edita campos pre-autorización (folio, fechas, conceptos). No
    /// tocará líneas — esas se editan con métodos específicos. Solo
    /// permite si está en <see cref="EstadoPasivo.Capturada"/>.
    /// </summary>
    public void EditarCabeceraPreAutorizacion(
        string? folioProveedor,
        string? serieProveedor,
        DateOnly fechaVencimiento,
        DateTimeOffset fechaContabilizacion)
    {
        AsegurarMutable("editar cabecera");
        if (Estado != EstadoPasivo.Capturada)
        {
            throw new BusinessRuleException(
                "FACTURA_NO_EDITABLE",
                $"Solo facturas en Capturada permiten edición de cabecera (actual: {Estado}).");
        }
        if (fechaVencimiento < DateOnly.FromDateTime(FechaDocumento.UtcDateTime))
            throw new BusinessRuleException(
                "FACTURA_VENCIMIENTO_INVALIDO",
                "La fecha de vencimiento no puede ser anterior a la fecha del documento.");

        FolioProveedor = folioProveedor;
        SerieProveedor = serieProveedor;
        FechaVencimiento = fechaVencimiento;
        FechaContabilizacion = fechaContabilizacion;
    }

    private void AsegurarMutable(string operacion)
    {
        if (Estado is EstadoPasivo.Autorizada or EstadoPasivo.Pagada or EstadoPasivo.Cancelada)
        {
            throw new BusinessRuleException(
                "FACTURA_INMUTABLE",
                $"No se puede {operacion} en estado {Estado} (§A7 — inmutabilidad post-autorización).");
        }
    }

    private void RegistrarTransicion(
        EstadoPasivo anterior,
        EstadoPasivo nuevo,
        DateTimeOffset ahora,
        string? motivo,
        Guid? usuarioId)
    {
        _bitacora.Add(new BitacoraEstadoFactura(
            id: Guid.CreateVersion7(),
            facturaProveedorId: Id,
            estadoAnterior: anterior,
            estadoNuevo: nuevo,
            ocurridoEn: ahora,
            motivo: motivo,
            usuarioId: usuarioId));
    }
}
