using Millet.Compras.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Aggregate root del submódulo Órdenes de Compra (módulo Compras). Modela
/// una OC de no-producción desde su captura (Borrador) hasta su cierre o
/// terminación. La state machine y las invariantes están documentadas en
/// el diseño §5 (transiciones) y §4.1 (estructura).
///
/// F1-PR1 introduce solo la cabecera mínima: campos estructurales + estado
/// inicial <see cref="EstadoOrdenCompra.Borrador"/>, sin métodos de
/// transición y sin entidades hijas. Las transiciones (EnviarAAutorizacion,
/// Autorizar, Cancelar, Rechazar) entran en F3. Las líneas
/// (<see cref="LineaOrdenCompra"/>), adjuntos y VOs de
/// logística/importación entran en F2.
///
/// Columnas auxiliares del §10.1 que no aparecen como propiedades del
/// agregado en F1-PR1 (ReferenciaProveedor, ContactoProveedor*,
/// InformacionLogistica*, InformacionImportacion*, DescuentoGlobal*,
/// GastosAdicionales, Redondeo) viven como shadow properties en la
/// configuración EF — quedan persistidas con NULL o default y se cablearán
/// al agregado en F2-PR2/F2-PR3 (ver
/// docs/modulos/compras-ordenes-compra/03-pr-breakdown.md §Fase 2).
///
/// Implementa:
/// <list type="bullet">
///   <item><see cref="IPerteneceAEmpresa"/>: query filter por empresa (ADR-0011).</item>
///   <item><see cref="IAuditable"/>: registro automático en <c>core.audit_log</c> (ADR-0008).</item>
///   <item><see cref="IFiscalmenteRelevante"/>: soft delete vía <c>DeletedAt</c> (ADR-0008).</item>
/// </list>
/// </summary>
public sealed class OrdenCompra : BaseEntity, IPerteneceAEmpresa, IAuditable, IFiscalmenteRelevante
{
    /// <summary>Default <see cref="Moneda"/> para OCs domésticas (decisión C1).</summary>
    public const string MonedaDefault = "MXN";

    public Guid EmpresaId { get; set; }

    public Folio Folio { get; private set; } = default!;

    /// <summary>Año del folio. Junto con sucursal y folio forma el UNIQUE de la tabla (§10.1).</summary>
    public short FolioAnio { get; private set; }

    public Guid ProveedorId { get; private set; }
    public Guid SucursalDestinoId { get; private set; }
    public Guid CondicionesPagoId { get; private set; }
    public Guid UsoPrincipalId { get; private set; }

    /// <summary>Moneda ISO 4217 (3 caracteres). Default <see cref="MonedaDefault"/>.</summary>
    public string Moneda { get; private set; } = MonedaDefault;

    /// <summary>
    /// Tipo de cambio manual contra MXN. Requerido si <see cref="Moneda"/> != MXN
    /// (CHECK constraint <c>ck_oc_tipo_cambio</c>). En MXN debe ser NULL.
    /// </summary>
    public decimal? TipoCambio { get; private set; }

    /// <summary>Usuario que captura la OC. Inmutable tras la creación.</summary>
    public Guid CompradorTitularId { get; private set; }

    /// <summary>Encargado de seguimiento de la OC. Reasignable (F2-PR2 lo cablea).</summary>
    public Guid EncargadoComprasId { get; private set; }

    public string? Observaciones { get; private set; }

    public bool SinRequisicionPrevia { get; private set; }
    public bool EsImportacion { get; private set; }
    public bool CotizacionExcepcionada { get; private set; }

    /// <summary>
    /// True mientras cualquiera de los 3 campos críticos de cabecera
    /// (proveedor, condiciones de pago, uso principal) siga siendo un
    /// sentinel TBD (F5-PR3). Una OC nace en borrador mínimo cuando se crea
    /// automáticamente desde la bifurcación de una RQ y el comprador todavía
    /// no ha capturado los datos faltantes. <see cref="EnviarAAutorizacion"/>
    /// falla si la OC sigue en este estado.
    /// </summary>
    public bool EsBorradorMinimo =>
        ProveedorId == OrdenCompraTbdSentinels.Proveedor
        || CondicionesPagoId == OrdenCompraTbdSentinels.CondicionesPago
        || UsoPrincipalId == OrdenCompraTbdSentinels.UsoPrincipal;

    /// <summary>Fecha de calendario del documento (ADR-0040: DateOnly, no instante).</summary>
    public DateOnly FechaDocumento { get; private set; }

    /// <summary>Set automáticamente al autorizar N2 (§5.3). Null mientras no esté <see cref="EstadoOrdenCompra.Autorizada"/>. Instante real (timestamptz), no fecha de calendario.</summary>
    public DateTimeOffset? FechaContabilizacion { get; private set; }

    /// <summary>Fecha de calendario de entrega esperada (ADR-0040: DateOnly).</summary>
    public DateOnly? FechaEntregaEsperada { get; private set; }

    public EstadoOrdenCompra Estado { get; private set; }
    public SubEstadoRecepcion SubEstadoRecepcion { get; private set; }
    public SubEstadoFacturacion SubEstadoFacturacion { get; private set; }
    public SubEstadoPago SubEstadoPago { get; private set; }

    /// <summary>Motivo cuando <see cref="SinRequisicionPrevia"/> es true (CHECK <c>ck_oc_sin_rq_motivo</c>).</summary>
    public string? MotivoSinRequisicion { get; private set; }

    /// <summary>FK al catálogo <c>motivos_rechazo</c> al cancelar (F3-PR3). Llena junto con <see cref="MotivoCancelacion"/>.</summary>
    public Guid? MotivoCancelacionId { get; private set; }

    /// <summary>Texto adicional del motivo al cancelar (F3-PR3). Junto con el FK forma el snapshot completo.</summary>
    public string? MotivoCancelacion { get; private set; }

    /// <summary>FK a <c>compras.motivos_rechazo</c> (reusa el catálogo de RQ). Se llena en F3-PR2.</summary>
    public Guid? MotivoRechazoId { get; private set; }

    /// <summary>Texto libre del rechazo si el motivo lo permite. Se llena en F3-PR2.</summary>
    public string? MotivoRechazoTexto { get; private set; }

    /// <summary>
    /// FK a la OC origen cuando esta OC nace de <c>DuplicarOrdenCompraCommand</c>
    /// (C4 — F6-PR2). Null si fue creada desde RQ o vacía.
    /// </summary>
    public Guid? OcOrigenId { get; private set; }

    // --- F2-PR1: descuento global, gastos, redondeo (inputs del cálculo de totales) ---

    /// <summary>
    /// Tipo de descuento global (diseño §4.8). Persistido como columna
    /// scalar; ambos nullables: NULL ⇔ sin descuento global. Combinado
    /// con <see cref="DescuentoGlobalValor"/> via <see cref="DescuentoGlobal"/>
    /// (getter que reconstruye la VO).
    /// </summary>
    public DescuentoTipo? DescuentoGlobalTipo { get; private set; }

    /// <summary>Valor del descuento global (porcentaje 0–100 o monto ≥ 0).</summary>
    public decimal? DescuentoGlobalValor { get; private set; }

    /// <summary>
    /// VO descuento global (diseño §4.8). Se reconstruye desde
    /// <see cref="DescuentoGlobalTipo"/> y <see cref="DescuentoGlobalValor"/>;
    /// devuelve <c>null</c> si cualquiera de los dos es null. Aplica sobre
    /// la suma de subtotales de línea (post-descuento por línea) antes de
    /// los gastos adicionales.
    /// </summary>
    public DescuentoGlobal? DescuentoGlobal =>
        DescuentoGlobalTipo.HasValue && DescuentoGlobalValor.HasValue
            ? new DescuentoGlobal(DescuentoGlobalTipo.Value, DescuentoGlobalValor.Value)
            : null;

    /// <summary>Gastos adicionales que se suman a la base gravable. Default 0.</summary>
    public decimal GastosAdicionales { get; private set; }

    /// <summary>Redondeo de cierre (positivo o negativo) aplicado al <c>TotalAPagar</c>. Default 0.</summary>
    public decimal Redondeo { get; private set; }

    /// <summary>
    /// Monto pagado acumulado (denormalizado, F5-PR1). Lo actualizan los
    /// listeners de Tesorería en F5-PR2 vía <see cref="RegistrarPago"/>.
    /// Se compara contra <see cref="TotalesOC.TotalAPagar"/> en
    /// <see cref="RecalcularSubEstados"/> para derivar <see cref="SubEstadoPago"/>.
    /// </summary>
    public decimal MontoPagado { get; private set; }

    /// <summary>
    /// Timestamp de cierre automático (F5-PR1). Null mientras no esté
    /// <see cref="EstadoOrdenCompra.Cerrada"/>; se setea cuando las 3
    /// dimensiones cierran.
    /// </summary>
    public DateTimeOffset? FechaCierre { get; private set; }

    // --- F2-PR3: referencia proveedor + contacto + logística + importación ---

    /// <summary>Folio externo del proveedor (§4.4). Indexado para búsqueda. Normalizado a uppercase + trim.</summary>
    public string? ReferenciaProveedor { get; private set; }

    // Contacto proveedor: 3 scalars + getter computed (mismo patrón que
    // DescuentoGlobal). Evita problemas de EF Core 9 con
    // ComplexProperty + nullable struct.
    public string? ContactoProveedorNombre { get; private set; }
    public string? ContactoProveedorEmail { get; private set; }
    public string? ContactoProveedorTelefono { get; private set; }

    public ContactoProveedor? ContactoProveedor =>
        ContactoProveedorNombre is null && ContactoProveedorEmail is null && ContactoProveedorTelefono is null
            ? null
            : new ContactoProveedor(ContactoProveedorNombre, ContactoProveedorEmail, ContactoProveedorTelefono);

    // Información logística: 5 scalars + getter computed.
    public string? InfoLogisticaDireccion { get; private set; }
    public Guid? InfoLogisticaTransportistaId { get; private set; }
    public string? InfoLogisticaTransportistaTexto { get; private set; }
    public string? InfoLogisticaNumeroGuia { get; private set; }
    public string? InfoLogisticaInstrucciones { get; private set; }

    public InformacionLogistica? InformacionLogistica =>
        InfoLogisticaDireccion is null && InfoLogisticaTransportistaId is null &&
        InfoLogisticaTransportistaTexto is null && InfoLogisticaNumeroGuia is null &&
        InfoLogisticaInstrucciones is null
            ? null
            : new InformacionLogistica(
                InfoLogisticaDireccion, InfoLogisticaTransportistaId,
                InfoLogisticaTransportistaTexto, InfoLogisticaNumeroGuia, InfoLogisticaInstrucciones);

    // Información importación: 6 scalars + getter computed.
    public Guid? InfoImportIncotermId { get; private set; }
    public string? InfoImportPaisOrigen { get; private set; }
    public string? InfoImportNumeroContenedor { get; private set; }
    public string? InfoImportCodigoRuta { get; private set; }
    public string? InfoImportSemanaEmbarque { get; private set; }
    public string? InfoImportNumeroPedimento { get; private set; }

    public InformacionImportacion? InformacionImportacion =>
        InfoImportIncotermId is null && InfoImportPaisOrigen is null &&
        InfoImportNumeroContenedor is null && InfoImportCodigoRuta is null &&
        InfoImportSemanaEmbarque is null && InfoImportNumeroPedimento is null
            ? null
            : new InformacionImportacion(
                InfoImportIncotermId, InfoImportPaisOrigen, InfoImportNumeroContenedor,
                InfoImportCodigoRuta, InfoImportSemanaEmbarque, InfoImportNumeroPedimento);

    // --- F2-PR1: líneas ---

    private readonly List<LineaOrdenCompra> _lineas = [];
    public IReadOnlyCollection<LineaOrdenCompra> Lineas => _lineas.AsReadOnly();

    // --- F2-PR4: adjuntos ---

    private readonly List<AdjuntoOC> _adjuntos = [];
    public IReadOnlyCollection<AdjuntoOC> Adjuntos => _adjuntos.AsReadOnly();

    // --- F3-PR1: autorizaciones ---

    private readonly List<AutorizacionOC> _autorizaciones = [];
    public IReadOnlyCollection<AutorizacionOC> Autorizaciones => _autorizaciones.AsReadOnly();

    /// <summary>Constructor para EF Core.</summary>
    private OrdenCompra() { }

    /// <summary>
    /// Construye una OC nueva en estado <see cref="EstadoOrdenCompra.Borrador"/>
    /// con cabecera mínima. Sub-estados arrancan en sus valores
    /// <c>SinX</c>. Aplica invariantes estructurales (GUIDs no vacíos,
    /// rango razonable de año, coherencia moneda/tipo de cambio, motivo
    /// si sin-rq).
    /// </summary>
    public OrdenCompra(
        Guid id,
        Guid empresaId,
        Folio folio,
        short folioAnio,
        Guid proveedorId,
        Guid sucursalDestinoId,
        Guid condicionesPagoId,
        Guid usoPrincipalId,
        Guid compradorTitularId,
        Guid encargadoComprasId,
        DateOnly fechaDocumento,
        string moneda = MonedaDefault,
        decimal? tipoCambio = null,
        bool sinRequisicionPrevia = false,
        bool esImportacion = false,
        bool cotizacionExcepcionada = false,
        string? observaciones = null,
        string? motivoSinRequisicion = null,
        DateOnly? fechaEntregaEsperada = null,
        Guid? ocOrigenId = null) : base(id)
    {
        EnsureNotEmpty(empresaId, nameof(empresaId));
        EnsureNotEmpty(proveedorId, nameof(proveedorId));
        EnsureNotEmpty(sucursalDestinoId, nameof(sucursalDestinoId));
        EnsureNotEmpty(condicionesPagoId, nameof(condicionesPagoId));
        EnsureNotEmpty(usoPrincipalId, nameof(usoPrincipalId));
        EnsureNotEmpty(compradorTitularId, nameof(compradorTitularId));
        EnsureNotEmpty(encargadoComprasId, nameof(encargadoComprasId));

        if (folioAnio < 2000 || folioAnio > 2100)
        {
            throw new BusinessRuleException(
                "FOLIO_OC_ANIO_INVALIDO",
                $"Año de folio fuera de rango razonable (2000-2100): {folioAnio}.");
        }

        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
        {
            throw new BusinessRuleException(
                "OC_MONEDA_INVALIDA",
                "La moneda debe ser un código ISO 4217 de 3 caracteres.");
        }

        // CHECK ck_oc_tipo_cambio: en MXN debe ser NULL; en moneda extranjera > 0.
        var monedaUpper = moneda.ToUpperInvariant();
        if (monedaUpper == MonedaDefault && tipoCambio is not null)
        {
            throw new BusinessRuleException(
                "OC_TIPO_CAMBIO_NO_APLICA",
                "La OC en MXN no debe tener tipo de cambio.");
        }
        if (monedaUpper != MonedaDefault && tipoCambio is not > 0)
        {
            throw new BusinessRuleException(
                "OC_TIPO_CAMBIO_REQUERIDO",
                $"La OC en {monedaUpper} requiere tipo de cambio mayor a cero.");
        }

        // CHECK ck_oc_sin_rq_motivo: si sin RQ previa, el motivo es obligatorio.
        if (sinRequisicionPrevia && string.IsNullOrWhiteSpace(motivoSinRequisicion))
        {
            throw new BusinessRuleException(
                "OC_MOTIVO_SIN_RQ_REQUERIDO",
                "Una OC sin requisición previa requiere capturar el motivo.");
        }

        if (observaciones is { Length: > 1000 })
        {
            throw new BusinessRuleException(
                "OC_OBSERVACIONES_DEMASIADO_LARGAS",
                "Las observaciones no pueden exceder 1000 caracteres.");
        }

        if (motivoSinRequisicion is { Length: > 500 })
        {
            throw new BusinessRuleException(
                "OC_MOTIVO_SIN_RQ_DEMASIADO_LARGO",
                "El motivo sin requisición no puede exceder 500 caracteres.");
        }

        if (ocOrigenId == id)
        {
            throw new BusinessRuleException(
                "OC_ORIGEN_AUTOREFERENCIA",
                "Una OC no puede tener como origen a sí misma.");
        }

        EmpresaId = empresaId;
        Folio = folio ?? throw new BusinessRuleException(
            "FOLIO_OC_REQUERIDO",
            "El folio es requerido para crear una orden de compra.");
        FolioAnio = folioAnio;
        ProveedorId = proveedorId;
        SucursalDestinoId = sucursalDestinoId;
        CondicionesPagoId = condicionesPagoId;
        UsoPrincipalId = usoPrincipalId;
        Moneda = monedaUpper;
        TipoCambio = tipoCambio;
        CompradorTitularId = compradorTitularId;
        EncargadoComprasId = encargadoComprasId;
        Observaciones = observaciones;
        SinRequisicionPrevia = sinRequisicionPrevia;
        EsImportacion = esImportacion;
        CotizacionExcepcionada = cotizacionExcepcionada;
        FechaDocumento = fechaDocumento;
        FechaEntregaEsperada = fechaEntregaEsperada;
        MotivoSinRequisicion = motivoSinRequisicion;
        OcOrigenId = ocOrigenId;
        Estado = EstadoOrdenCompra.Borrador;
        SubEstadoRecepcion = SubEstadoRecepcion.SinRecepcion;
        SubEstadoFacturacion = SubEstadoFacturacion.SinFactura;
        SubEstadoPago = SubEstadoPago.SinPago;
    }

    // --- F2-PR1/F2-PR2: gestión de cabecera y líneas ---

    private static readonly EstadoOrdenCompra[] EstadosEditables =
    [
        EstadoOrdenCompra.Borrador,
        EstadoOrdenCompra.Rechazada,
    ];

    private static readonly EstadoOrdenCompra[] EstadosTerminales =
    [
        EstadoOrdenCompra.Cerrada,
        EstadoOrdenCompra.Cancelada,
        EstadoOrdenCompra.Rechazada,
    ];

    /// <summary>
    /// Editar cabecera (F2-PR2): muta los campos editables en estado
    /// <see cref="EstadoOrdenCompra.Borrador"/> o
    /// <see cref="EstadoOrdenCompra.Rechazada"/>. Cualquier parámetro
    /// nullable que llegue como <c>null</c> NO se modifica (PATCH
    /// parcial). Para limpiar un nullable a null se usan los flags
    /// <c>limpiarX</c>.
    ///
    /// <para>
    /// Inmutables (requieren recrear la OC vía Cancelar + Duplicar C4):
    /// EmpresaId, Folio/FolioAnio, SucursalDestinoId, CompradorTitularId,
    /// SinRequisicionPrevia, OcOrigenId.
    /// </para>
    ///
    /// <para>
    /// Notas sobre invariantes:
    /// <list type="bullet">
    ///   <item>Si cambia <c>Moneda</c>, el caller debe pasar también
    ///         <c>tipoCambio</c> coherente (la validación es la misma del
    ///         ctor).</item>
    ///   <item>Si cambia <c>EsImportacion</c> a <c>true</c>, los campos de
    ///         info importación se completan en
    ///         <c>ActualizarInformacionImportacion</c> (F2-PR3).</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <summary>
    /// Completa la cabecera de una OC borrador mínima (F5-PR3): reemplaza
    /// cada campo TBD por el valor real. Solo permitido si la OC sigue
    /// en <see cref="EstadoOrdenCompra.Borrador"/> y todavía es
    /// <see cref="EsBorradorMinimo"/>. Pasar <c>null</c> para un campo no
    /// lo toca — útil si el comprador completa la cabecera por partes.
    ///
    /// <para>
    /// Si tras la operación todos los TBD se reemplazaron, la OC sale del
    /// estado borrador-mínimo. El handler que invoca este método publica
    /// un <c>OrdenCompraBorradorCompletadoEvent</c> en F5-PR3 (opcional;
    /// la transición de estado real es <c>EnviarAAutorizacion</c>).
    /// </para>
    /// </summary>
    public void CompletarCabeceraBorrador(
        Guid? proveedorId,
        Guid? condicionesPagoId,
        Guid? usoPrincipalId)
    {
        if (Estado != EstadoOrdenCompra.Borrador)
        {
            throw new BusinessRuleException(
                "OC_COMPLETAR_BORRADOR_SOLO_BORRADOR",
                $"Solo se puede completar la cabecera de un borrador en estado Borrador (actual: {Estado}).");
        }

        if (proveedorId is Guid p)
        {
            EnsureNotEmpty(p, nameof(proveedorId));
            if (p == OrdenCompraTbdSentinels.Proveedor)
            {
                throw new BusinessRuleException(
                    "OC_COMPLETAR_BORRADOR_VALOR_TBD",
                    "No se puede completar con el propio sentinel TBD.");
            }
            ProveedorId = p;
        }
        if (condicionesPagoId is Guid c)
        {
            EnsureNotEmpty(c, nameof(condicionesPagoId));
            if (c == OrdenCompraTbdSentinels.CondicionesPago)
            {
                throw new BusinessRuleException(
                    "OC_COMPLETAR_BORRADOR_VALOR_TBD",
                    "No se puede completar con el propio sentinel TBD.");
            }
            CondicionesPagoId = c;
        }
        if (usoPrincipalId is Guid u)
        {
            EnsureNotEmpty(u, nameof(usoPrincipalId));
            if (u == OrdenCompraTbdSentinels.UsoPrincipal)
            {
                throw new BusinessRuleException(
                    "OC_COMPLETAR_BORRADOR_VALOR_TBD",
                    "No se puede completar con el propio sentinel TBD.");
            }
            UsoPrincipalId = u;
        }
    }

    public void ActualizarCabecera(
        Guid? proveedorId,
        Guid? condicionesPagoId,
        Guid? usoPrincipalId,
        Guid? encargadoComprasId,
        string? moneda,
        decimal? tipoCambio,
        bool? esImportacion,
        bool? cotizacionExcepcionada,
        string? observaciones,
        DateOnly? fechaEntregaEsperada,
        DescuentoTipo? descuentoGlobalTipo,
        decimal? descuentoGlobalValor,
        decimal? gastosAdicionales,
        decimal? redondeo,
        bool limpiarObservaciones = false,
        bool limpiarFechaEntregaEsperada = false,
        bool limpiarDescuentoGlobal = false,
        bool limpiarTipoCambio = false)
    {
        if (!EstadosEditables.Contains(Estado))
        {
            throw new BusinessRuleException(
                "OC_EDITAR_CABECERA_SOLO_EDITABLE",
                $"La cabecera solo puede editarse en Borrador o Rechazada (actual: {Estado}).");
        }

        // Asignaciones simples — no-nullable solo si llega valor.
        if (proveedorId is Guid p)
        {
            EnsureNotEmpty(p, nameof(proveedorId));
            ProveedorId = p;
        }
        if (condicionesPagoId is Guid c)
        {
            EnsureNotEmpty(c, nameof(condicionesPagoId));
            CondicionesPagoId = c;
        }
        if (usoPrincipalId is Guid u)
        {
            EnsureNotEmpty(u, nameof(usoPrincipalId));
            UsoPrincipalId = u;
        }
        if (encargadoComprasId is Guid e)
        {
            EnsureNotEmpty(e, nameof(encargadoComprasId));
            EncargadoComprasId = e;
        }

        // Moneda + TipoCambio: la coherencia se valida juntas (los
        // invariantes del ctor aplican). El caller que cambie a moneda
        // extranjera debe pasar tipoCambio > 0 en el mismo call.
        if (moneda is not null)
        {
            if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            {
                throw new BusinessRuleException(
                    "OC_MONEDA_INVALIDA",
                    "La moneda debe ser un código ISO 4217 de 3 caracteres.");
            }
            var monedaUpper = moneda.ToUpperInvariant();
            var nuevoTipoCambio = tipoCambio ?? (limpiarTipoCambio ? null : TipoCambio);

            if (monedaUpper == MonedaDefault && nuevoTipoCambio is not null)
            {
                throw new BusinessRuleException(
                    "OC_TIPO_CAMBIO_NO_APLICA",
                    "La OC en MXN no debe tener tipo de cambio.");
            }
            if (monedaUpper != MonedaDefault && nuevoTipoCambio is not > 0)
            {
                throw new BusinessRuleException(
                    "OC_TIPO_CAMBIO_REQUERIDO",
                    $"La OC en {monedaUpper} requiere tipo de cambio mayor a cero.");
            }
            Moneda = monedaUpper;
            TipoCambio = nuevoTipoCambio;
        }
        else if (tipoCambio is not null || limpiarTipoCambio)
        {
            var nuevoTipoCambio = tipoCambio ?? (limpiarTipoCambio ? null : TipoCambio);
            if (Moneda == MonedaDefault && nuevoTipoCambio is not null)
            {
                throw new BusinessRuleException(
                    "OC_TIPO_CAMBIO_NO_APLICA",
                    "La OC en MXN no debe tener tipo de cambio.");
            }
            if (Moneda != MonedaDefault && nuevoTipoCambio is not > 0)
            {
                throw new BusinessRuleException(
                    "OC_TIPO_CAMBIO_REQUERIDO",
                    $"La OC en {Moneda} requiere tipo de cambio mayor a cero.");
            }
            TipoCambio = nuevoTipoCambio;
        }

        if (esImportacion is bool ei) EsImportacion = ei;
        if (cotizacionExcepcionada is bool ce) CotizacionExcepcionada = ce;

        if (observaciones is not null)
        {
            if (observaciones.Length > 1000)
            {
                throw new BusinessRuleException(
                    "OC_OBSERVACIONES_DEMASIADO_LARGAS",
                    "Las observaciones no pueden exceder 1000 caracteres.");
            }
            Observaciones = observaciones;
        }
        else if (limpiarObservaciones) Observaciones = null;

        if (fechaEntregaEsperada is not null) FechaEntregaEsperada = fechaEntregaEsperada;
        else if (limpiarFechaEntregaEsperada) FechaEntregaEsperada = null;

        // DescuentoGlobal: tratado como pareja. Si llega cualquiera de
        // los dos, ambos deben coexistir (el getter computed asume eso).
        // Si limpiar = true, ambos a null.
        if (limpiarDescuentoGlobal)
        {
            DescuentoGlobalTipo = null;
            DescuentoGlobalValor = null;
        }
        else if (descuentoGlobalTipo is DescuentoTipo dt || descuentoGlobalValor is decimal _)
        {
            var tipo = descuentoGlobalTipo ?? DescuentoGlobalTipo
                ?? throw new BusinessRuleException(
                    "OC_DESCUENTO_GLOBAL_TIPO_REQUERIDO",
                    "El tipo de descuento global es requerido cuando se setea valor.");
            var valor = descuentoGlobalValor ?? DescuentoGlobalValor
                ?? throw new BusinessRuleException(
                    "OC_DESCUENTO_GLOBAL_VALOR_REQUERIDO",
                    "El valor de descuento global es requerido cuando se setea tipo.");

            // Validar via el VO (lanza si está fuera de rango).
            _ = new DescuentoGlobal(tipo, valor);

            DescuentoGlobalTipo = tipo;
            DescuentoGlobalValor = valor;
        }

        if (gastosAdicionales is decimal ga)
        {
            if (ga < 0m)
            {
                throw new BusinessRuleException(
                    "OC_GASTOS_NEGATIVOS",
                    "Los gastos adicionales no pueden ser negativos.");
            }
            GastosAdicionales = ga;
        }

        if (redondeo is decimal r) Redondeo = r;
    }

    /// <summary>
    /// Agrega una línea manual (sin FK a RQ). Solo permitido si la OC está
    /// en <c>Borrador</c>/<c>Rechazada</c> Y tiene la bandera
    /// <see cref="SinRequisicionPrevia"/> = true (§4.3 del mapa funcional —
    /// el flujo de "agregar desde RQ" entra en F4-PR2). La posición se
    /// asigna automáticamente como la siguiente disponible.
    /// </summary>
    public LineaOrdenCompra AgregarLineaManual(
        Guid lineaId,
        Guid articuloId,
        decimal cantidad,
        string unidadMedida,
        decimal precioUnitario,
        Guid departamentoSolicitanteId,
        DescuentoLinea? descuento = null,
        IndicadorImpuestos? indicadorImpuestos = null,
        string? descripcionExtendida = null,
        DateTimeOffset? fechaEntregaLinea = null,
        string? textoAdicional = null,
        bool esServicio = false,
        Guid? centroCostoId = null)
    {
        if (!EstadosEditables.Contains(Estado))
        {
            throw new BusinessRuleException(
                "OC_LINEAS_SOLO_EN_BORRADOR_O_RECHAZADA",
                $"Solo se pueden agregar líneas en Borrador o Rechazada (actual: {Estado}).");
        }

        if (!SinRequisicionPrevia)
        {
            throw new BusinessRuleException(
                "OC_LINEA_MANUAL_REQUIERE_SIN_RQ",
                "Solo se pueden agregar líneas manuales si la OC tiene SinRequisicionPrevia = true.");
        }

        var siguientePosicion = _lineas.Count == 0
            ? 1
            : _lineas.Max(l => l.Posicion) + 1;

        var linea = new LineaOrdenCompra(
            id: lineaId,
            ordenCompraId: Id,
            posicion: siguientePosicion,
            articuloId: articuloId,
            cantidad: cantidad,
            unidadMedida: unidadMedida,
            precioUnitario: precioUnitario,
            departamentoSolicitanteId: departamentoSolicitanteId,
            descuento: descuento,
            indicadorImpuestos: indicadorImpuestos,
            requisicionId: null,
            lineaRequisicionId: null,
            centroCostoId: centroCostoId,
            descripcionExtendida: descripcionExtendida,
            fechaEntregaLinea: fechaEntregaLinea,
            textoAdicional: textoAdicional,
            esServicio: esServicio);

        _lineas.Add(linea);
        return linea;
    }

    /// <summary>
    /// Agrega una línea con FK a una línea de RQ (flujo §4.1 / §4.2 del
    /// mapa funcional — F4-PR2). Solo permitido si la OC está en
    /// <c>Borrador</c>/<c>Rechazada</c> Y <see cref="SinRequisicionPrevia"/>
    /// es <c>false</c> (las dos banderas son mutuamente excluyentes —
    /// una OC consolida RQs O captura manual, no ambos).
    ///
    /// **Política de preservación §3.bis.4**: mismo <c>articuloId</c> de
    /// RQs distintas crea líneas separadas (no se suman). La agrupación
    /// para el PDF al proveedor es solo cosmética.
    ///
    /// El caller (handler) es responsable de:
    /// <list type="bullet">
    ///   <item>Verificar que la RQ esté Autorizada + no comprometida.</item>
    ///   <item>Llamar <see cref="Domain.Requisicion.ComprometerEnOc"/> en la misma TX.</item>
    ///   <item>Validar que <c>lineaRq.SucursalId == oc.SucursalDestinoId</c>
    ///         (restricción §10.5).</item>
    /// </list>
    /// </summary>
    public LineaOrdenCompra AgregarLineaDesdeRequisicion(
        Guid lineaId,
        Guid articuloId,
        decimal cantidad,
        string unidadMedida,
        decimal precioUnitario,
        Guid departamentoSolicitanteId,
        Guid requisicionId,
        Guid lineaRequisicionId,
        DescuentoLinea? descuento = null,
        IndicadorImpuestos? indicadorImpuestos = null,
        string? descripcionExtendida = null,
        DateTimeOffset? fechaEntregaLinea = null,
        string? textoAdicional = null,
        bool esServicio = false,
        Guid? centroCostoId = null)
    {
        if (!EstadosEditables.Contains(Estado))
        {
            throw new BusinessRuleException(
                "OC_LINEAS_SOLO_EN_BORRADOR_O_RECHAZADA",
                $"Solo se pueden agregar líneas en Borrador o Rechazada (actual: {Estado}).");
        }

        if (SinRequisicionPrevia)
        {
            throw new BusinessRuleException(
                "OC_LINEA_DESDE_RQ_INCOMPATIBLE",
                "No se pueden agregar líneas desde RQ a una OC con SinRequisicionPrevia = true.");
        }

        if (requisicionId == Guid.Empty || lineaRequisicionId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "OC_LINEA_RQ_IDS_REQUERIDOS",
                "RequisicionId y LineaRequisicionId son obligatorios al agregar línea desde RQ.");
        }

        var siguientePosicion = _lineas.Count == 0
            ? 1
            : _lineas.Max(l => l.Posicion) + 1;

        var linea = new LineaOrdenCompra(
            id: lineaId,
            ordenCompraId: Id,
            posicion: siguientePosicion,
            articuloId: articuloId,
            cantidad: cantidad,
            unidadMedida: unidadMedida,
            precioUnitario: precioUnitario,
            departamentoSolicitanteId: departamentoSolicitanteId,
            descuento: descuento,
            indicadorImpuestos: indicadorImpuestos,
            requisicionId: requisicionId,
            lineaRequisicionId: lineaRequisicionId,
            centroCostoId: centroCostoId,
            descripcionExtendida: descripcionExtendida,
            fechaEntregaLinea: fechaEntregaLinea,
            textoAdicional: textoAdicional,
            esServicio: esServicio);

        _lineas.Add(linea);
        return linea;
    }

    /// <summary>
    /// Actualiza los campos estructurales de una línea (artículo, cantidad,
    /// precio, descuento, etc.). Permitido solo en <c>Borrador</c>/<c>Rechazada</c>
    /// y solo si la línea aún no tiene recepción o facturación
    /// (§4.2 invariante). PATCH parcial: nullables = no tocar.
    /// </summary>
    public void ActualizarLinea(
        Guid lineaId,
        Guid? articuloId = null,
        decimal? cantidad = null,
        string? unidadMedida = null,
        decimal? precioUnitario = null,
        DescuentoLinea? descuento = null,
        IndicadorImpuestos? indicadorImpuestos = null,
        Guid? departamentoSolicitanteId = null,
        string? descripcionExtendida = null,
        DateTimeOffset? fechaEntregaLinea = null,
        bool limpiarDescripcionExtendida = false,
        bool limpiarFechaEntregaLinea = false,
        bool? esServicio = null,
        Guid? centroCostoId = null)
    {
        if (!EstadosEditables.Contains(Estado))
        {
            throw new BusinessRuleException(
                "OC_LINEAS_SOLO_EN_BORRADOR_O_RECHAZADA",
                $"Solo se pueden modificar líneas en Borrador o Rechazada (actual: {Estado}).");
        }

        var linea = BuscarLineaOLanzar(lineaId);
        linea.ActualizarEstructural(
            articuloId, cantidad, unidadMedida, precioUnitario,
            descuento, indicadorImpuestos,
            departamentoSolicitanteId, descripcionExtendida,
            fechaEntregaLinea, limpiarDescripcionExtendida,
            limpiarFechaEntregaLinea, esServicio, centroCostoId);
    }

    /// <summary>
    /// Elimina una línea. Solo permitido en <c>Borrador</c>/<c>Rechazada</c>
    /// y si la línea no tiene recepción ni facturación (la integridad
    /// referencial post-recepción la cubre F5-PR4 cancelación parcial).
    /// </summary>
    public EliminarLineaResultado EliminarLinea(Guid lineaId)
    {
        if (!EstadosEditables.Contains(Estado))
        {
            throw new BusinessRuleException(
                "OC_LINEAS_SOLO_EN_BORRADOR_O_RECHAZADA",
                $"Solo se pueden eliminar líneas en Borrador o Rechazada (actual: {Estado}).");
        }

        var linea = BuscarLineaOLanzar(lineaId);
        if (linea.CantidadRecibida > 0m || linea.CantidadFacturada > 0m)
        {
            throw new BusinessRuleException(
                "OC_LINEA_CON_RECEPCION_NO_ELIMINABLE",
                "No se puede eliminar una línea con recepción o facturación registrada.");
        }

        var requisicionIdEliminada = linea.RequisicionId;
        _lineas.Remove(linea);

        // F4-PR3: si la línea tenía FK a RQ y era la última de esa RQ
        // en la OC, devolver la RQ para que el handler la libere via
        // listener cross-aggregate.
        if (requisicionIdEliminada is Guid rqId
            && !_lineas.Any(l => l.RequisicionId == rqId))
        {
            return new EliminarLineaResultado(rqId);
        }

        return new EliminarLineaResultado(null);
    }

    /// <summary>
    /// Actualiza solo el <see cref="LineaOrdenCompra.TextoAdicional"/> de
    /// una línea. Permitido en cualquier estado **no terminal** — útil
    /// para anotaciones operativas durante el ciclo de la OC (§4.2
    /// invariante: campos estructurales se bloquean post-recepción, pero
    /// el texto sigue editable).
    /// </summary>
    public void ActualizarTextoAdicionalLinea(Guid lineaId, string? textoAdicional)
    {
        if (EstadosTerminales.Contains(Estado))
        {
            throw new BusinessRuleException(
                "OC_TEXTO_ADICIONAL_NO_EN_TERMINAL",
                $"No se puede editar el texto adicional de una OC en estado terminal (actual: {Estado}).");
        }

        var linea = BuscarLineaOLanzar(lineaId);
        linea.ActualizarTextoAdicional(textoAdicional);
    }

    private LineaOrdenCompra BuscarLineaOLanzar(Guid lineaId)
    {
        return _lineas.FirstOrDefault(l => l.Id == lineaId)
            ?? throw new BusinessRuleException(
                "OC_LINEA_NO_ENCONTRADA",
                $"La OC no contiene la línea {lineaId}.");
    }

    // --- F2-PR4: gestión de adjuntos ---

    /// <summary>
    /// Adjunta un documento a la OC (diseño §4.11). Permitido en
    /// cualquier estado **no terminal** — adjuntar sigue posible
    /// post-autorización (e.g. agregar pedimento o factura proveedor
    /// extranjero al recibir). El blob ya debe estar subido al storage;
    /// este método solo persiste la metadata.
    /// </summary>
    public AdjuntoOC AdjuntarDocumento(
        Guid adjuntoId,
        Guid tipoDocumentoId,
        string nombreArchivo,
        string blobUrl,
        string contentType,
        long tamañoBytes,
        DateTimeOffset fechaCarga,
        Guid usuarioCargaId)
    {
        if (EstadosTerminales.Contains(Estado))
        {
            throw new BusinessRuleException(
                "OC_ADJUNTO_NO_EN_TERMINAL",
                $"No se pueden adjuntar documentos en estado terminal (actual: {Estado}).");
        }

        var adjunto = new AdjuntoOC(
            id: adjuntoId,
            ordenCompraId: Id,
            tipoDocumentoId: tipoDocumentoId,
            nombreArchivo: nombreArchivo,
            blobUrl: blobUrl,
            contentType: contentType,
            tamañoBytes: tamañoBytes,
            fechaCarga: fechaCarga,
            usuarioCargaId: usuarioCargaId);

        _adjuntos.Add(adjunto);
        return adjunto;
    }

    /// <summary>
    /// Remueve un adjunto. Solo permitido en <c>Borrador</c> — una vez
    /// transmitida la OC, los adjuntos quedan para auditoría (§4.11
    /// invariante). Devuelve la <see cref="AdjuntoOC.BlobUrl"/> para
    /// que el caller borre el blob físico vía el puerto
    /// <c>IAlmacenarBlobPort.EliminarAsync</c>.
    /// </summary>
    public string RemoverAdjunto(Guid adjuntoId)
    {
        if (Estado != EstadoOrdenCompra.Borrador)
        {
            throw new BusinessRuleException(
                "OC_REMOVER_ADJUNTO_SOLO_BORRADOR",
                $"Solo se pueden remover adjuntos en estado Borrador (actual: {Estado}).");
        }

        var adjunto = _adjuntos.FirstOrDefault(a => a.Id == adjuntoId)
            ?? throw new BusinessRuleException(
                "OC_ADJUNTO_NO_ENCONTRADO",
                $"La OC no contiene el adjunto {adjuntoId}.");

        _adjuntos.Remove(adjunto);
        return adjunto.BlobUrl;
    }

    // --- F3-PR1: workflow de autorización ---

    /// <summary>
    /// Transmite la OC desde <see cref="EstadoOrdenCompra.Borrador"/> o
    /// <see cref="EstadoOrdenCompra.Rechazada"/> a
    /// <see cref="EstadoOrdenCompra.EnAutorizacionJefeCompras"/>.
    /// Valida invariantes intrínsecas del agregado:
    /// <list type="bullet">
    ///   <item>Estado es Borrador o Rechazada.</item>
    ///   <item>Al menos 1 línea.</item>
    /// </list>
    ///
    /// <para>
    /// Las validaciones cross-table (proveedor activo C10, cotización
    /// adjunta o excepción + correo C11, ficha técnica si importación,
    /// motivo + correo si sin-rq) las hace el handler en
    /// <c>EnviarAAutorizacionHandler</c> ANTES de invocar este método,
    /// porque requieren conocer claves del catálogo
    /// <c>tipos_documento_oc</c> que el agregado no debe conocer.
    /// </para>
    ///
    /// <para>
    /// Devuelve el evento <see cref="Events.OrdenCompraEnviadaAAutorizacionEvent"/>
    /// que el handler publica vía MediatR INotification tras SaveChanges.
    /// </para>
    /// </summary>
    public Events.OrdenCompraEnviadaAAutorizacionEvent EnviarAAutorizacion(DateTimeOffset ocurridoEn)
    {
        if (Estado != EstadoOrdenCompra.Borrador && Estado != EstadoOrdenCompra.Rechazada)
        {
            throw new BusinessRuleException(
                "OC_TRANSMITIR_SOLO_DESDE_BORRADOR_O_RECHAZADA",
                $"Solo se puede transmitir desde Borrador o Rechazada (actual: {Estado}).");
        }

        if (_lineas.Count == 0)
        {
            throw new BusinessRuleException(
                "OC_TRANSMITIR_SIN_LINEAS",
                "La OC debe tener al menos una línea para enviarse a autorización.");
        }

        if (EsBorradorMinimo)
        {
            throw new BusinessRuleException(
                "OC_TRANSMITIR_BORRADOR_MINIMO",
                "La OC todavía tiene campos TBD en la cabecera (proveedor / condiciones / uso / almacén). Completar antes de transmitir.");
        }

        Estado = EstadoOrdenCompra.EnAutorizacionJefeCompras;

        return new Events.OrdenCompraEnviadaAAutorizacionEvent(
            OrdenCompraId: Id,
            EmpresaId: EmpresaId,
            Folio: Folio.Valor,
            CompradorTitularId: CompradorTitularId,
            OcurridoEn: ocurridoEn);
    }

    /// <summary>
    /// Registra una autorización exitosa (firma) en el nivel indicado y
    /// transiciona el estado de la OC:
    /// <list type="bullet">
    ///   <item><see cref="NivelAutorizacion.Nivel1"/> desde
    ///         <c>EnAutorizacionJefeCompras</c> → <c>EnAutorizacionDireccion</c>.</item>
    ///   <item><see cref="NivelAutorizacion.Nivel2"/> desde
    ///         <c>EnAutorizacionDireccion</c> → <c>Autorizada</c>; setea
    ///         <see cref="FechaContabilizacion"/>; emite
    ///         <see cref="Events.OrdenCompraAutorizadaEvent"/>.</item>
    /// </list>
    /// </summary>
    public AutorizarResultado Autorizar(
        Guid autorizacionId,
        NivelAutorizacion nivel,
        Guid usuarioId,
        DateTimeOffset fechaHora,
        string? notas = null)
    {
        var estadoEsperado = nivel switch
        {
            NivelAutorizacion.Nivel1 => EstadoOrdenCompra.EnAutorizacionJefeCompras,
            NivelAutorizacion.Nivel2 => EstadoOrdenCompra.EnAutorizacionDireccion,
            _ => throw new BusinessRuleException(
                "OC_AUTORIZAR_NIVEL_INVALIDO",
                $"Nivel de autorización inválido: {nivel}."),
        };

        if (Estado != estadoEsperado)
        {
            throw new BusinessRuleException(
                "OC_AUTORIZAR_ESTADO_INCORRECTO",
                $"No se puede autorizar {nivel} en estado {Estado}; estado esperado: {estadoEsperado}.");
        }

        if (_autorizaciones.Any(a =>
            a.Nivel == nivel && a.Resultado == ResultadoAutorizacionOc.Autorizado))
        {
            throw new BusinessRuleException(
                "OC_AUTORIZACION_NIVEL_DUPLICADO",
                $"Ya existe una autorización exitosa de nivel {nivel} para esta OC.");
        }

        // N2 requiere N1 autorizada previa (defense in depth — el estado
        // ya lo garantiza por la state machine).
        if (nivel == NivelAutorizacion.Nivel2
            && !_autorizaciones.Any(a =>
                a.Nivel == NivelAutorizacion.Nivel1 && a.Resultado == ResultadoAutorizacionOc.Autorizado))
        {
            throw new BusinessRuleException(
                "OC_AUTORIZACION_NIVEL2_SIN_NIVEL1",
                "Nivel2 requiere que primero exista Nivel1 autorizada.");
        }

        var autorizacion = new AutorizacionOC(
            id: autorizacionId,
            ordenCompraId: Id,
            nivel: nivel,
            usuarioId: usuarioId,
            fechaHora: fechaHora,
            notas: notas);
        _autorizaciones.Add(autorizacion);

        Events.OrdenCompraAutorizadaEvent? autorizadaEvento = null;
        if (nivel == NivelAutorizacion.Nivel1)
        {
            Estado = EstadoOrdenCompra.EnAutorizacionDireccion;
        }
        else
        {
            Estado = EstadoOrdenCompra.Autorizada;
            FechaContabilizacion = fechaHora;
            autorizadaEvento = new Events.OrdenCompraAutorizadaEvent(
                OrdenCompraId: Id,
                EmpresaId: EmpresaId,
                Folio: Folio.Valor,
                CompradorTitularId: CompradorTitularId,
                FechaContabilizacion: fechaHora,
                OcurridoEn: fechaHora);
        }

        return new AutorizarResultado(autorizacion, autorizadaEvento);
    }

    /// <summary>
    /// Rechaza una OC en cualquier nivel del flujo de autorización
    /// (transición <c>EnAutorizacionJefeCompras</c> o
    /// <c>EnAutorizacionDireccion</c> → <c>Rechazada</c>). Crea una
    /// <see cref="AutorizacionOC"/> con
    /// <see cref="ResultadoAutorizacionOc.Rechazado"/> y llena los
    /// campos de motivo en la OC para preservar la causa en el shape
    /// del agregado (además de la fila en autorizaciones).
    ///
    /// El nivel se infiere del estado actual de la OC:
    /// <list type="bullet">
    ///   <item>Estado <c>EnAutorizacionJefeCompras</c> → rechazo N1.</item>
    ///   <item>Estado <c>EnAutorizacionDireccion</c> → rechazo N2.</item>
    /// </list>
    ///
    /// El handler valida cross-table que el motivo aplica al flujo de
    /// OC (bitmask <c>MotivoRechazoAplicaA.OrdenCompra = 8</c>) y que
    /// exista texto si el motivo lo requiere.
    /// </summary>
    public Events.OrdenCompraRechazadaEvent Rechazar(
        Guid autorizacionId,
        Guid usuarioId,
        DateTimeOffset fechaHora,
        Guid motivoRechazoId,
        string? motivoRechazoTexto = null,
        string? notas = null)
    {
        var nivel = Estado switch
        {
            EstadoOrdenCompra.EnAutorizacionJefeCompras => NivelAutorizacion.Nivel1,
            EstadoOrdenCompra.EnAutorizacionDireccion => NivelAutorizacion.Nivel2,
            _ => throw new BusinessRuleException(
                "OC_RECHAZAR_ESTADO_INVALIDO",
                $"Solo se puede rechazar desde EnAutorizacionJefeCompras o EnAutorizacionDireccion (actual: {Estado})."),
        };

        if (motivoRechazoId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "OC_RECHAZAR_MOTIVO_REQUERIDO",
                "El motivo de rechazo es requerido.");
        }

        var autorizacion = new AutorizacionOC(
            id: autorizacionId,
            ordenCompraId: Id,
            nivel: nivel,
            usuarioId: usuarioId,
            fechaHora: fechaHora,
            motivoRechazoId: motivoRechazoId,
            motivoRechazoTexto: motivoRechazoTexto,
            notas: notas);
        _autorizaciones.Add(autorizacion);

        Estado = EstadoOrdenCompra.Rechazada;
        MotivoRechazoId = motivoRechazoId;
        MotivoRechazoTexto = motivoRechazoTexto;

        return new Events.OrdenCompraRechazadaEvent(
            OrdenCompraId: Id,
            EmpresaId: EmpresaId,
            Folio: Folio.Valor,
            NivelRechazo: nivel,
            MotivoRechazoId: motivoRechazoId,
            MotivoRechazoTexto: motivoRechazoTexto,
            CompradorTitularId: CompradorTitularId,
            UsuarioRechazadorId: usuarioId,
            OcurridoEn: fechaHora);
    }

    /// <summary>
    /// Cancela una OC en cualquier estado no terminal donde
    /// <see cref="SubEstadoRecepcion"/> == <see cref="SubEstadoRecepcion.SinRecepcion"/>.
    /// La cancelación con recepciones parciales requiere doble
    /// autorización y libera RQs proporcionalmente — entra en F5-PR4.
    ///
    /// Transición a <see cref="EstadoOrdenCompra.Cancelada"/>. Llena
    /// <see cref="MotivoCancelacionId"/> + <see cref="MotivoCancelacion"/>.
    /// Devuelve evento <see cref="Events.OrdenCompraCanceladaEvent"/>
    /// con el estado previo para que listeners (Notificaciones,
    /// Requisiciones — liberación) sepan el contexto.
    /// </summary>
    public CancelarResultado Cancelar(
        Guid usuarioId,
        DateTimeOffset fechaHora,
        Guid motivoCancelacionId,
        string? motivoCancelacionTexto = null)
    {
        if (EstadosTerminales.Contains(Estado))
        {
            throw new BusinessRuleException(
                "OC_CANCELAR_ESTADO_TERMINAL",
                $"No se puede cancelar una OC en estado terminal (actual: {Estado}).");
        }

        if (SubEstadoRecepcion != SubEstadoRecepcion.SinRecepcion)
        {
            throw new BusinessRuleException(
                "OC_CANCELAR_CON_RECEPCION",
                "Cancelar OCs con recepciones parciales requiere doble autorización (F5-PR4 maneja ese caso).");
        }

        if (motivoCancelacionId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "OC_CANCELAR_MOTIVO_REQUERIDO",
                "El motivo de cancelación es requerido.");
        }

        if (motivoCancelacionTexto is { Length: > 500 })
        {
            throw new BusinessRuleException(
                "OC_CANCELAR_TEXTO_DEMASIADO_LARGO",
                "El texto del motivo no puede exceder 500 caracteres.");
        }

        var estadoPrevio = Estado;
        Estado = EstadoOrdenCompra.Cancelada;
        MotivoCancelacionId = motivoCancelacionId;
        MotivoCancelacion = motivoCancelacionTexto;

        // F4-PR3: recolectar RQs únicas comprometidas para que el
        // handler libere cada una vía LineaRqLiberadaEvent.
        var rqsALiberar = _lineas
            .Where(l => l.RequisicionId.HasValue)
            .Select(l => l.RequisicionId!.Value)
            .Distinct()
            .ToList();

        var evento = new Events.OrdenCompraCanceladaEvent(
            OrdenCompraId: Id,
            EmpresaId: EmpresaId,
            Folio: Folio.Valor,
            EstadoPrevio: estadoPrevio,
            MotivoCancelacionId: motivoCancelacionId,
            MotivoCancelacionTexto: motivoCancelacionTexto,
            CompradorTitularId: CompradorTitularId,
            UsuarioCanceladorId: usuarioId,
            OcurridoEn: fechaHora);

        return new CancelarResultado(evento, rqsALiberar);
    }

    /// <summary>
    /// Cancela una OC que tiene <see cref="SubEstadoRecepcion.Parcial"/>
    /// o <see cref="SubEstadoRecepcion.Completa"/> en recepción (F5-PR4).
    /// Las cantidades ya recibidas permanecen en las líneas (no se
    /// decrementan) para preservar la trazabilidad contable. Para cada
    /// línea con RQ asociada y <c>cantidad_recibida &lt; cantidad</c>,
    /// se reporta una <see cref="LineaRqLiberacionParcial"/> con la
    /// <c>cantidad_no_recibida</c>; el handler emite un
    /// <see cref="Events.LineaRqLiberadaEvent"/> con esa cantidad.
    ///
    /// <para>
    /// Líneas con <c>cantidad_recibida == cantidad</c> NO generan
    /// liberación (no hay nada que devolver al pool de RQ). La RQ
    /// asociada solo se libera de la OC si tiene al menos una línea
    /// parcialmente liberada — caso opuesto, queda comprometida
    /// históricamente con esta OC ya cancelada (trazabilidad).
    /// </para>
    ///
    /// <para>
    /// La validación de doble autorización (3 permisos requeridos) la
    /// hace el endpoint API antes de invocar este método. El agregado
    /// solo valida invariantes propias (no terminal + motivo).
    /// </para>
    /// </summary>
    public CancelarConRecepcionesResultado CancelarConRecepcionesParciales(
        Guid usuarioId,
        DateTimeOffset fechaHora,
        Guid motivoCancelacionId,
        string? motivoCancelacionTexto = null)
    {
        if (EstadosTerminales.Contains(Estado))
        {
            throw new BusinessRuleException(
                "OC_CANCELAR_ESTADO_TERMINAL",
                $"No se puede cancelar una OC en estado terminal (actual: {Estado}).");
        }

        if (motivoCancelacionId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "OC_CANCELAR_MOTIVO_REQUERIDO",
                "El motivo de cancelación es requerido.");
        }

        if (motivoCancelacionTexto is { Length: > 500 })
        {
            throw new BusinessRuleException(
                "OC_CANCELAR_TEXTO_DEMASIADO_LARGO",
                "El texto del motivo no puede exceder 500 caracteres.");
        }

        var estadoPrevio = Estado;
        Estado = EstadoOrdenCompra.Cancelada;
        MotivoCancelacionId = motivoCancelacionId;
        MotivoCancelacion = motivoCancelacionTexto;

        // Recolectar liberaciones parciales: solo líneas con RQ y saldo
        // no recibido > 0.
        var liberaciones = _lineas
            .Where(l => l.RequisicionId.HasValue && l.Cantidad > l.CantidadRecibida)
            .Select(l => new LineaRqLiberacionParcial(
                RequisicionId: l.RequisicionId!.Value,
                LineaOrdenCompraId: l.Id,
                CantidadLiberada: l.Cantidad - l.CantidadRecibida))
            .ToList();

        var evento = new Events.OrdenCompraCanceladaEvent(
            OrdenCompraId: Id,
            EmpresaId: EmpresaId,
            Folio: Folio.Valor,
            EstadoPrevio: estadoPrevio,
            MotivoCancelacionId: motivoCancelacionId,
            MotivoCancelacionTexto: motivoCancelacionTexto,
            CompradorTitularId: CompradorTitularId,
            UsuarioCanceladorId: usuarioId,
            OcurridoEn: fechaHora);

        return new CancelarConRecepcionesResultado(evento, liberaciones);
    }

    /// <summary>
    /// Recalcula los impuestos (motor v0 IVA 16%) de todas las líneas. Se
    /// invoca tras cambios masivos en líneas o régimen aplicable. F3-PR2
    /// reemplaza el motor.
    /// </summary>
    public void RecalcularImpuestos()
    {
        foreach (var linea in _lineas)
        {
            linea.RecalcularImpuestos();
        }
    }

    // --- F5-PR1: sub-estados materializados + cierre automático ---

    /// <summary>
    /// Registra (set, no incrementa) la cantidad acumulada recibida para
    /// una línea (F5-PR1). El payload de los listeners trae el acumulado
    /// — repetir el mismo evento es no-op. Solo permitido si la OC está
    /// <see cref="EstadoOrdenCompra.Autorizada"/> o <see cref="EstadoOrdenCompra.Cerrada"/>
    /// (una devolución que reabre, F5-PR2, vuelve a Autorizada primero).
    ///
    /// <para>
    /// Tras setear, invoca <see cref="RecalcularSubEstados"/> y devuelve
    /// el evento <see cref="Events.OrdenCompraCerradaEvent"/> si la OC
    /// cerró automáticamente (las 3 dimensiones completas). El handler
    /// publica el evento tras SaveChanges.
    /// </para>
    /// </summary>
    public RecalcularSubEstadosResultado RegistrarRecepcionLinea(
        Guid lineaId,
        decimal cantidadAcumulada,
        DateTimeOffset ocurridoEn)
    {
        ValidarEstadoParaRegistrarMovimiento();
        var linea = BuscarLineaOLanzar(lineaId);
        linea.SetCantidadRecibida(cantidadAcumulada);
        return RecalcularSubEstadosYAjustarEstado(ocurridoEn);
    }

    /// <summary>
    /// Registra el acumulado facturado para una línea. Mismo contrato que
    /// <see cref="RegistrarRecepcionLinea"/>. F5-PR2 conecta los listeners
    /// de CxP que invocan este método.
    /// </summary>
    public RecalcularSubEstadosResultado RegistrarFacturacionLinea(
        Guid lineaId,
        decimal cantidadAcumulada,
        DateTimeOffset ocurridoEn)
    {
        ValidarEstadoParaRegistrarMovimiento();
        var linea = BuscarLineaOLanzar(lineaId);
        linea.SetCantidadFacturada(cantidadAcumulada);
        return RecalcularSubEstadosYAjustarEstado(ocurridoEn);
    }

    /// <summary>
    /// Registra (set) el monto pagado acumulado a nivel cabecera. Los
    /// pagos viven en Tesorería y se aplican a facturas; el agregado
    /// solo mantiene el denormalizado para derivar el sub-estado de pago
    /// (decisión v1 del diseño §5.2 — F8 puede mover esto a vista
    /// materializada si la denormalización causa drift).
    /// </summary>
    public RecalcularSubEstadosResultado RegistrarPago(
        decimal montoPagadoAcumulado,
        DateTimeOffset ocurridoEn)
    {
        if (montoPagadoAcumulado < 0m)
        {
            throw new BusinessRuleException(
                "OC_MONTO_PAGADO_NEGATIVO",
                "El monto pagado acumulado no puede ser negativo.");
        }
        ValidarEstadoParaRegistrarMovimiento();
        MontoPagado = montoPagadoAcumulado;
        return RecalcularSubEstadosYAjustarEstado(ocurridoEn);
    }

    /// <summary>
    /// Recalcula los 3 sub-estados a partir del estado actual de las
    /// líneas y de <see cref="MontoPagado"/>. NO transiciona el estado
    /// principal — la transición a <see cref="EstadoOrdenCompra.Cerrada"/>
    /// la dispara <see cref="RecalcularSubEstadosYCerrarSiAplica"/>.
    /// Útil para reproyectar tras cambios masivos.
    /// </summary>
    public void RecalcularSubEstados()
    {
        if (_lineas.Count == 0)
        {
            SubEstadoRecepcion = SubEstadoRecepcion.SinRecepcion;
            SubEstadoFacturacion = SubEstadoFacturacion.SinFactura;
            SubEstadoPago = SubEstadoPago.SinPago;
            return;
        }

        var cantidadTotal = _lineas.Sum(l => l.Cantidad);
        var facturadoTotal = _lineas.Sum(l => l.CantidadFacturada);

        // GAP-9: los servicios no se reciben en Almacén — el sub-estado de
        // Recepción se calcula SOLO sobre las líneas físicas (!EsServicio).
        // Si la OC es 100% servicios, el subconjunto queda vacío y el
        // sub-estado permanece SinRecepcion (NO Completa: la regla de
        // cancelación sin recepciones depende de SinRecepcion). Facturación
        // y Pago siguen calculándose sobre todas las líneas.
        var lineasFisicas = _lineas.Where(l => !l.EsServicio).ToList();
        var cantidadFisicaTotal = lineasFisicas.Sum(l => l.Cantidad);
        var recibidoTotal = lineasFisicas.Sum(l => l.CantidadRecibida);

        SubEstadoRecepcion = recibidoTotal switch
        {
            0m => SubEstadoRecepcion.SinRecepcion,
            var r when r >= cantidadFisicaTotal => SubEstadoRecepcion.Completa,
            _ => SubEstadoRecepcion.Parcial,
        };

        SubEstadoFacturacion = facturadoTotal switch
        {
            0m => SubEstadoFacturacion.SinFactura,
            var f when f >= cantidadTotal => SubEstadoFacturacion.Completa,
            _ => SubEstadoFacturacion.Parcial,
        };

        var totalAPagar = CalcularTotales().TotalAPagar;
        SubEstadoPago = MontoPagado switch
        {
            0m => SubEstadoPago.SinPago,
            var p when totalAPagar > 0m && p >= totalAPagar => SubEstadoPago.Pagada,
            _ => SubEstadoPago.Parcial,
        };
    }

    private RecalcularSubEstadosResultado RecalcularSubEstadosYAjustarEstado(DateTimeOffset ocurridoEn)
    {
        RecalcularSubEstados();

        // GAP-9 (verificación e2e 2026-07-16): una OC 100% de servicios
        // nunca registra recepciones (los servicios no pasan por Almacén),
        // así que su SubEstadoRecepcion queda SinRecepcion de por vida. Para
        // que estas OCs puedan cerrar automáticamente, la dimensión de
        // recepción se considera satisfecha cuando todas las líneas son de
        // servicio. En OCs mixtas la recepción de las líneas físicas sigue
        // siendo obligatoria (SubEstadoRecepcion se calcula solo sobre ellas).
        var recepcionOk =
            SubEstadoRecepcion == SubEstadoRecepcion.Completa
            || (_lineas.Count > 0 && _lineas.All(l => l.EsServicio));

        var todasCerradas =
            recepcionOk
            && SubEstadoFacturacion == SubEstadoFacturacion.Completa
            && SubEstadoPago == SubEstadoPago.Pagada;

        // Cierre automático: solo desde Autorizada con 3 dimensiones completas.
        if (Estado == EstadoOrdenCompra.Autorizada && todasCerradas)
        {
            Estado = EstadoOrdenCompra.Cerrada;
            FechaCierre = ocurridoEn;
            var cerrada = new Events.OrdenCompraCerradaEvent(
                OrdenCompraId: Id,
                EmpresaId: EmpresaId,
                Folio: Folio.Valor,
                CompradorTitularId: CompradorTitularId,
                OcurridoEn: ocurridoEn);
            return new RecalcularSubEstadosResultado(cerrada, null);
        }

        // Reabrir: Cerrada → Autorizada si alguna dimensión salió de su
        // estado de cierre (devolución, nota de crédito).
        if (Estado == EstadoOrdenCompra.Cerrada && !todasCerradas)
        {
            Estado = EstadoOrdenCompra.Autorizada;
            FechaCierre = null;
            var reabierta = new Events.OrdenCompraReabriertaEvent(
                OrdenCompraId: Id,
                EmpresaId: EmpresaId,
                Folio: Folio.Valor,
                CompradorTitularId: CompradorTitularId,
                OcurridoEn: ocurridoEn);
            return new RecalcularSubEstadosResultado(null, reabierta);
        }

        return new RecalcularSubEstadosResultado(null, null);
    }

    /// <summary>
    /// Cierre manual de la OC (GAP-9). Válvula de escape para OCs que no
    /// alcanzan el cierre automático — típicamente servicios o residuales
    /// (saldos menores que el proveedor nunca surtirá/facturará). Solo
    /// permitido desde <see cref="EstadoOrdenCompra.Autorizada"/>; setea
    /// <see cref="EstadoOrdenCompra.Cerrada"/> + <see cref="FechaCierre"/>
    /// y devuelve el mismo <see cref="Events.OrdenCompraCerradaEvent"/>
    /// que emite el cierre automático (los suscriptores no distinguen el
    /// origen del cierre). El handler lo publica tras SaveChanges.
    /// </summary>
    public Events.OrdenCompraCerradaEvent CerrarManual(DateTimeOffset ocurridoEn)
    {
        if (Estado != EstadoOrdenCompra.Autorizada)
        {
            throw new BusinessRuleException(
                "OC_CERRAR_MANUAL_ESTADO_INCORRECTO",
                $"Solo se puede cerrar manualmente una OC Autorizada (actual: {Estado}).");
        }

        Estado = EstadoOrdenCompra.Cerrada;
        FechaCierre = ocurridoEn;

        return new Events.OrdenCompraCerradaEvent(
            OrdenCompraId: Id,
            EmpresaId: EmpresaId,
            Folio: Folio.Valor,
            CompradorTitularId: CompradorTitularId,
            OcurridoEn: ocurridoEn);
    }

    private void ValidarEstadoParaRegistrarMovimiento()
    {
        if (Estado != EstadoOrdenCompra.Autorizada && Estado != EstadoOrdenCompra.Cerrada)
        {
            throw new BusinessRuleException(
                "OC_MOVIMIENTO_ESTADO_INVALIDO",
                $"Solo se pueden registrar movimientos en Autorizada o Cerrada (actual: {Estado}).");
        }
    }

    // --- F2-PR3: referencia proveedor, contacto, logística, importación ---

    /// <summary>
    /// Actualiza el folio externo del proveedor. Permitido en Borrador o
    /// Rechazada. Normaliza a uppercase + trim. <c>null</c> limpia el
    /// campo.
    /// </summary>
    public void ActualizarReferenciaProveedor(string? referencia)
    {
        if (!EstadosEditables.Contains(Estado))
        {
            throw new BusinessRuleException(
                "OC_REFERENCIA_SOLO_EDITABLE",
                $"La referencia proveedor solo puede editarse en Borrador o Rechazada (actual: {Estado}).");
        }
        if (referencia is { Length: > 60 })
        {
            throw new BusinessRuleException(
                "OC_REFERENCIA_DEMASIADO_LARGA",
                "La referencia proveedor no puede exceder 60 caracteres.");
        }
        ReferenciaProveedor = string.IsNullOrWhiteSpace(referencia)
            ? null
            : referencia.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Actualiza el snapshot del contacto del proveedor. Permitido en
    /// Borrador o Rechazada. <c>null</c> limpia los 3 campos.
    /// </summary>
    public void ActualizarContactoProveedor(ContactoProveedor? contacto)
    {
        if (!EstadosEditables.Contains(Estado))
        {
            throw new BusinessRuleException(
                "OC_CONTACTO_SOLO_EDITABLE",
                $"El contacto proveedor solo puede editarse en Borrador o Rechazada (actual: {Estado}).");
        }
        ContactoProveedorNombre = contacto?.Nombre;
        ContactoProveedorEmail = contacto?.Email;
        ContactoProveedorTelefono = contacto?.Telefono;
    }

    /// <summary>
    /// Actualiza la información logística. Permitido en Borrador,
    /// Rechazada o Autorizada (§4.6 — transportista, guía y contenedor
    /// cambian durante el ciclo sin re-autorización). <c>null</c> limpia
    /// todos los campos.
    /// </summary>
    public void ActualizarInformacionLogistica(InformacionLogistica? info)
    {
        if (EstadosTerminales.Contains(Estado) && Estado != EstadoOrdenCompra.Rechazada)
        {
            // Permitido en Borrador, Rechazada, EnAutorizacionJefeCompras,
            // EnAutorizacionDireccion y Autorizada. Bloquea Cerrada y
            // Cancelada.
            throw new BusinessRuleException(
                "OC_LOGISTICA_NO_EN_TERMINAL",
                $"La información logística no puede editarse en estado terminal (actual: {Estado}).");
        }
        InfoLogisticaDireccion = info?.DireccionEntrega;
        InfoLogisticaTransportistaId = info?.TransportistaId;
        InfoLogisticaTransportistaTexto = info?.TransportistaTexto;
        InfoLogisticaNumeroGuia = info?.NumeroGuia;
        InfoLogisticaInstrucciones = info?.InstruccionesEnvio;
    }

    /// <summary>
    /// Actualiza la información de importación. Permitido en Borrador o
    /// Rechazada, **excepto** <c>NumeroPedimento</c> que se captura
    /// post-autorización vía
    /// <see cref="ActualizarNumeroPedimento"/> (§4.7).
    /// </summary>
    public void ActualizarInformacionImportacion(InformacionImportacion? info)
    {
        if (!EstadosEditables.Contains(Estado))
        {
            throw new BusinessRuleException(
                "OC_IMPORTACION_SOLO_EDITABLE",
                $"La información de importación solo puede editarse en Borrador o Rechazada (actual: {Estado}).");
        }
        InfoImportIncotermId = info?.IncotermId;
        InfoImportPaisOrigen = info?.PaisOrigen;
        InfoImportNumeroContenedor = info?.NumeroContenedor;
        InfoImportCodigoRuta = info?.CodigoRuta;
        InfoImportSemanaEmbarque = info?.SemanaEmbarque;
        // NumeroPedimento NO se setea aquí — se captura post-autorización.
        // Si el llamador envía un valor en `info?.NumeroPedimento`, se
        // ignora (mejor que sorprender al cliente).
    }

    /// <summary>
    /// Actualiza solo el <c>NumeroPedimento</c>. Permitido post-autorización
    /// (§4.7 — se captura al recibir el material). Bloquea estados
    /// terminales (Cerrada/Cancelada).
    /// </summary>
    public void ActualizarNumeroPedimento(string? numeroPedimento)
    {
        if (Estado == EstadoOrdenCompra.Cerrada || Estado == EstadoOrdenCompra.Cancelada)
        {
            throw new BusinessRuleException(
                "OC_PEDIMENTO_NO_EN_TERMINAL",
                $"NumeroPedimento no puede editarse en estado terminal (actual: {Estado}).");
        }
        if (numeroPedimento is { Length: > 60 })
        {
            throw new BusinessRuleException(
                "IMPORT_PEDIMENTO_DEMASIADO_LARGO",
                "NumeroPedimento no puede exceder 60 caracteres.");
        }
        InfoImportNumeroPedimento = string.IsNullOrWhiteSpace(numeroPedimento) ? null : numeroPedimento;
    }

    /// <summary>
    /// Calcula los totales de la OC (diseño §4.9). Lectura — no muta el
    /// agregado. Inputs:
    /// <list type="bullet">
    ///   <item>Líneas: cada una contribuye con <see cref="LineaOrdenCompra.SubtotalLinea"/>,
    ///         <see cref="LineaOrdenCompra.IvaImporte"/> y
    ///         <see cref="LineaOrdenCompra.RetencionIsr"/>.</item>
    ///   <item>Cabecera: <see cref="DescuentoGlobal"/>,
    ///         <see cref="GastosAdicionales"/>, <see cref="Redondeo"/>.</item>
    /// </list>
    /// </summary>
    public TotalesOC CalcularTotales()
    {
        var subtotalAntesDescuento = _lineas.Sum(l => l.SubtotalLinea);
        var descuentoGlobalAplicado = DescuentoGlobal?.Aplicar(subtotalAntesDescuento) ?? 0m;
        var baseGravable = subtotalAntesDescuento - descuentoGlobalAplicado + GastosAdicionales;
        var ivaTotal = _lineas.Sum(l => l.IvaImporte);
        var retencionIsrTotal = _lineas.Sum(l => l.RetencionIsr ?? 0m);
        var totalAPagar = baseGravable + ivaTotal - retencionIsrTotal + Redondeo;

        return new TotalesOC(
            SubtotalAntesDescuento: subtotalAntesDescuento,
            DescuentoGlobalAplicado: descuentoGlobalAplicado,
            GastosAdicionales: GastosAdicionales,
            BaseGravable: baseGravable,
            IvaTotal: ivaTotal,
            RetencionIsrTotal: retencionIsrTotal,
            Redondeo: Redondeo,
            TotalAPagar: totalAPagar);
    }

    private static void EnsureNotEmpty(Guid value, string nombre)
    {
        if (value == Guid.Empty)
        {
            throw new BusinessRuleException(
                "GUID_VACIO",
                $"El campo '{nombre}' no puede ser Guid.Empty.");
        }
    }
}
