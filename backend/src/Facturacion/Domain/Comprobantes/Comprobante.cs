using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Comprobantes;

/// <summary>
/// Base abstracta de todo CFDI emitido (§3.bis.2, §4.1 diseño). Concentra los
/// datos fiscales comunes, la FSM de timbrado y los datos del timbre. Las
/// especializaciones (<c>FacturaVenta</c>, y más adelante <c>FacturaAnticipo</c>,
/// <c>NotaCredito</c>, <c>CartaPorte</c>, <c>ReciboPago</c>) agregan sus campos.
/// EF Core la mapea con <b>table-per-type (TPT)</b>: tabla base
/// <c>comprobante</c> + una tabla por subtipo.
///
/// <para>
/// Multi-tenant (<see cref="IPerteneceAEmpresa"/>) + fiscalmente relevante
/// (<see cref="IFiscalmenteRelevante"/>): el query filter del BaseDbContext
/// aplica empresa + soft-delete a toda la jerarquía (el filtro vive en la raíz
/// — ver <c>BaseDbContext.ApplyQueryFilters</c>).
/// </para>
///
/// <para>
/// F1-PR1 sólo ejercita el camino feliz síncrono de la FSM
/// (<c>Borrador → TimbradoEnProceso → Timbrado | TimbradoFallido</c>) contra el
/// stub de timbrado. <c>PendientePedimento</c> (F7) y la cancelación (F5)
/// existen en el enum pero no se transicionan aquí.
/// </para>
/// </summary>
public abstract class Comprobante : BaseEntity, IPerteneceAEmpresa, IFiscalmenteRelevante, IAuditable
{
    public Guid EmpresaId { get; set; }

    public TipoComprobante Tipo { get; private set; }

    /// <summary>
    /// Folio interno formateado, reservado atómicamente vía
    /// <c>Compartido.Series</c> (incluye prefijo de serie + consecutivo). La
    /// separación en atributos XML <c>Serie</c>/<c>Folio</c> del CFDI se
    /// resuelve al construir el XML real (F12).
    /// </summary>
    public string Folio { get; private set; } = string.Empty;

    /// <summary>Consecutivo numérico del folio (para ordenamiento/reportes).</summary>
    public long FolioNumero { get; private set; }

    public Guid SucursalId { get; private set; }

    /// <summary>
    /// Canal de venta del comprobante (`compartido.canales_venta`), dimensión
    /// de la Capa A de Cajas junto con <see cref="SucursalId"/>
    /// (`[Decisión 12-D]`, 12-cajas.md §7). Elevado desde <c>FacturaVenta</c>
    /// para que NC, REPP y anticipos hereden dimensiones y el alcance se
    /// evalúe uniforme sobre la raíz TPT. Null = comprobante sin canal
    /// (históricos pre-caja o Carta Porte) → bucket "Sin asignar".
    /// </summary>
    public short? CanalVentaId { get; private set; }

    public Guid? CajaId { get; private set; }
    public Guid? UsuarioEmisorId { get; private set; }

    // ---- Receptor (snapshot al emitir, §4.2) ----
    public string ReceptorRfc { get; private set; } = string.Empty;
    public string ReceptorNombre { get; private set; } = string.Empty;
    public string ReceptorRegimenFiscal { get; private set; } = string.Empty;
    public string ReceptorCodigoPostal { get; private set; } = string.Empty;
    public string ReceptorUsoCfdi { get; private set; } = string.Empty;
    public string ReceptorPais { get; private set; } = "MEX";
    public bool ReceptorEsGenerico { get; private set; }

    // ---- Emisor (snapshot al emitir, F12-PR1) ----
    public string RfcEmisor { get; private set; } = string.Empty;
    public string RegimenFiscalEmisor { get; private set; } = string.Empty;

    /// <summary>Razón social del emisor (Emisor@Nombre del CFDI 4.0). Vacío solo en comprobantes históricos pre-F12.</summary>
    public string NombreEmisor { get; private set; } = string.Empty;

    /// <summary>CP fiscal del emisor (LugarExpedicion del CFDI 4.0). Vacío solo en comprobantes históricos pre-F12.</summary>
    public string LugarExpedicion { get; private set; } = string.Empty;

    // ---- Datos de pago ----
    public string MetodoPago { get; private set; } = "PUE";
    public string FormaPago { get; private set; } = string.Empty;
    public string Moneda { get; private set; } = "MXN";
    public decimal? TipoCambio { get; private set; }

    // ---- Totales (calculados desde las líneas del subtipo) ----
    public decimal Subtotal { get; protected set; }
    public decimal Descuento { get; protected set; }
    public decimal ImpuestosTrasladados { get; protected set; }
    public decimal Retenciones { get; protected set; }
    public decimal Total { get; protected set; }

    public EstadoTimbrado Estado { get; private set; }

    // ---- Período contable (candado, D13) ----
    public int PeriodoAnio { get; private set; }
    public int PeriodoMes { get; private set; }

    // ---- Datos del timbre (null hasta Timbrado) ----
    public string? Uuid { get; private set; }
    public string? SelloCfdi { get; private set; }
    public string? SelloSat { get; private set; }
    public string? NoCertificadoSat { get; private set; }
    public DateTimeOffset? FechaTimbrado { get; private set; }
    public string? RfcProveedorCertificacion { get; private set; }

    /// <summary>
    /// Folio que el PAC asignó al CFDI (atributo <c>Folio</c> del XML).
    /// FiscalAPI lo calcula internamente (consecutivo por RFC emisor) y no
    /// acepta folios del cliente; el <see cref="Folio"/> interno de Millet
    /// sigue siendo la referencia de control operativo.
    /// </summary>
    public string? FolioPac { get; private set; }

    /// <summary>Referencia al archivo de CFDI en el repo común (Decisión 01-B).</summary>
    public Guid? CfdiArchivoId { get; private set; }

    // ---- Error de timbrado (si TimbradoFallido) ----
    public string? TimbradoErrorCodigo { get; private set; }
    public string? TimbradoErrorMensaje { get; private set; }

    public bool EnviadoCorreo { get; private set; }

    // ---- Relaciones CFDI (nodo CfdiRelacionados; §4.4) ----
    private readonly List<RelacionCfdi> _relaciones = [];

    /// <summary>
    /// Comprobantes relacionados por UUID (07 anticipo, 01 NC del original, 03
    /// devolución, 04 sustitución). Las llena el flujo de emisión (p.ej. la
    /// amortización de anticipos relaciona la factura de anticipo y la final).
    /// </summary>
    public IReadOnlyCollection<RelacionCfdi> Relaciones => _relaciones.AsReadOnly();

    /// <summary>Constructor EF Core.</summary>
    protected Comprobante() { }

    protected Comprobante(
        Guid id,
        Guid empresaId,
        TipoComprobante tipo,
        string folio,
        long folioNumero,
        Guid sucursalId,
        Guid? cajaId,
        Guid? usuarioEmisorId,
        DatosFiscalesReceptor receptor,
        DatosFiscalesEmisor emisor,
        string metodoPago,
        string formaPago,
        string moneda,
        decimal? tipoCambio,
        int periodoAnio,
        int periodoMes) : base(id)
    {
        ArgumentNullException.ThrowIfNull(receptor);
        ArgumentNullException.ThrowIfNull(emisor);
        if (string.IsNullOrWhiteSpace(folio))
            throw new BusinessRuleException("COMPROBANTE_FOLIO_INVALIDO", "El folio es obligatorio.");
        if (string.IsNullOrWhiteSpace(receptor.Rfc))
            throw new BusinessRuleException("COMPROBANTE_RECEPTOR_RFC_INVALIDO", "El RFC del receptor es obligatorio.");
        emisor.Validar();
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("COMPROBANTE_MONEDA_INVALIDA", "La moneda debe ser código ISO 4217 de 3 letras.");
        if (periodoMes is < 1 or > 12)
            throw new BusinessRuleException("COMPROBANTE_PERIODO_INVALIDO", "El mes del período debe estar entre 1 y 12.");

        EmpresaId = empresaId;
        Tipo = tipo;
        Folio = folio;
        FolioNumero = folioNumero;
        SucursalId = sucursalId;
        CajaId = cajaId;
        UsuarioEmisorId = usuarioEmisorId;

        ReceptorRfc = receptor.Rfc.ToUpperInvariant();
        ReceptorNombre = receptor.Nombre;
        ReceptorRegimenFiscal = receptor.RegimenFiscal;
        ReceptorCodigoPostal = receptor.CodigoPostal;
        ReceptorUsoCfdi = receptor.UsoCfdi;
        ReceptorPais = string.IsNullOrWhiteSpace(receptor.Pais) ? "MEX" : receptor.Pais.ToUpperInvariant();
        ReceptorEsGenerico = receptor.EsGenerico;

        RfcEmisor = emisor.Rfc.ToUpperInvariant();
        RegimenFiscalEmisor = emisor.RegimenFiscal;
        NombreEmisor = emisor.Nombre;
        LugarExpedicion = emisor.LugarExpedicion;
        MetodoPago = metodoPago;
        FormaPago = formaPago;
        Moneda = moneda.ToUpperInvariant();
        TipoCambio = tipoCambio;
        PeriodoAnio = periodoAnio;
        PeriodoMes = periodoMes;

        Estado = EstadoTimbrado.Borrador;
    }

    /// <summary>
    /// Transición <c>Borrador → TimbradoEnProceso</c>. El handler la invoca
    /// justo antes de llamar al PAC para que el comprobante no se considere un
    /// borrador editable mientras el timbre está en vuelo (Decisión 01-C).
    /// </summary>
    public void MarcarTimbradoEnProceso()
    {
        if (Estado != EstadoTimbrado.Borrador)
            throw new BusinessRuleException(
                "COMPROBANTE_NO_TIMBRABLE",
                $"Solo un comprobante en Borrador puede pasar a TimbradoEnProceso (actual: {Estado}).");

        Estado = EstadoTimbrado.TimbradoEnProceso;
    }

    /// <summary>
    /// Transición <c>TimbradoEnProceso → Timbrado</c>: registra UUID, sellos y
    /// la referencia al archivo de CFDI común. El comprobante queda inmutable.
    /// </summary>
    public void MarcarTimbrado(
        string uuid,
        string? selloCfdi,
        string? selloSat,
        string? noCertificadoSat,
        DateTimeOffset fechaTimbrado,
        string? rfcProveedorCertificacion,
        Guid? cfdiArchivoId,
        string? folioPac = null)
    {
        if (Estado != EstadoTimbrado.TimbradoEnProceso)
            throw new BusinessRuleException(
                "COMPROBANTE_NO_EN_PROCESO",
                $"Solo un comprobante en TimbradoEnProceso puede marcarse Timbrado (actual: {Estado}).");
        if (string.IsNullOrWhiteSpace(uuid))
            throw new BusinessRuleException("COMPROBANTE_UUID_INVALIDO", "El UUID del timbre es obligatorio.");

        Estado = EstadoTimbrado.Timbrado;
        Uuid = uuid;
        SelloCfdi = selloCfdi;
        SelloSat = selloSat;
        NoCertificadoSat = noCertificadoSat;
        FechaTimbrado = fechaTimbrado;
        RfcProveedorCertificacion = rfcProveedorCertificacion;
        CfdiArchivoId = cfdiArchivoId;
        FolioPac = folioPac;
        TimbradoErrorCodigo = null;
        TimbradoErrorMensaje = null;
    }

    /// <summary>
    /// Transición <c>TimbradoEnProceso → TimbradoFallido</c>: error definitivo
    /// del PAC. Corregible — <see cref="ReabrirParaReintentoTimbrado"/> lo
    /// regresa a Borrador para re-timbrar con el mismo folio interno.
    /// </summary>
    public void MarcarTimbradoFallido(string? errorCodigo, string? errorMensaje)
    {
        if (Estado != EstadoTimbrado.TimbradoEnProceso)
            throw new BusinessRuleException(
                "COMPROBANTE_NO_EN_PROCESO",
                $"Solo un comprobante en TimbradoEnProceso puede marcarse TimbradoFallido (actual: {Estado}).");

        Estado = EstadoTimbrado.TimbradoFallido;
        TimbradoErrorCodigo = errorCodigo;
        TimbradoErrorMensaje = errorMensaje;
    }

    /// <summary>
    /// Transición <c>TimbradoFallido → Borrador</c>: reabre el comprobante
    /// para reintentar el timbrado. Un rechazo del PAC no timbró nada ante el
    /// SAT — el documento sigue siendo válido y el folio interno ya está
    /// reservado, así que se re-timbra el MISMO comprobante (no se re-emite
    /// con folio nuevo). El último error se conserva para trazabilidad;
    /// <see cref="MarcarTimbrado"/> lo limpia si el reintento triunfa y
    /// <see cref="MarcarTimbradoFallido"/> lo reemplaza si vuelve a fallar.
    /// La distinción rechazo-limpio vs código-ambiguo (riesgo de timbre
    /// duplicado) la impone el handler, no el dominio.
    /// </summary>
    public void ReabrirParaReintentoTimbrado()
    {
        if (Estado != EstadoTimbrado.TimbradoFallido)
            throw new BusinessRuleException(
                "COMPROBANTE_NO_REINTENTABLE",
                $"Solo un comprobante en TimbradoFallido admite reintento de timbrado (actual: {Estado}).");

        Estado = EstadoTimbrado.Borrador;
    }

    /// <summary>
    /// Transición <c>TimbradoFallido → Descartada</c> ([Decisión 01-G] G3):
    /// el documento no se reintentará (pedido cancelado en origen, captura
    /// errónea de raíz). Terminal — quema el folio interno a conciencia; el
    /// último error del PAC se conserva para trazabilidad. Nunca hubo CFDI
    /// ante el SAT, así que no hay nada que cancelar.
    /// </summary>
    public void Descartar()
    {
        if (Estado != EstadoTimbrado.TimbradoFallido)
            throw new BusinessRuleException(
                "COMPROBANTE_NO_DESCARTABLE",
                $"Solo un comprobante en TimbradoFallido puede descartarse (actual: {Estado}).");

        Estado = EstadoTimbrado.Descartada;
    }

    /// <summary>Marca el CFDI como enviado por correo al cliente. Idempotente.</summary>
    public void MarcarEnviadoCorreo() => EnviadoCorreo = true;

    /// <summary>
    /// Fija el canal de venta del comprobante. Lo invocan los constructores de
    /// los subtipos (la exigencia varía por subtipo: <c>FacturaVenta</c> lo
    /// requiere; NC lo hereda de la factura relacionada; REPP/anticipo lo
    /// reciben del comando; Carta Porte no lo maneja).
    /// </summary>
    protected void EstablecerCanalVenta(short? canalVentaId)
    {
        if (canalVentaId is <= 0)
            throw new BusinessRuleException("COMPROBANTE_CANAL_INVALIDO", "CanalVentaId debe ser positivo u omitirse.");
        CanalVentaId = canalVentaId;
    }

    /// <summary>
    /// Asigna la caja que registró el cobro de este comprobante — <b>única vía
    /// de escritura</b> de <see cref="CajaId"/> (12-cajas.md §6, CAJAS-PR4).
    /// <c>CajaId</c> es Capa B (sesión que cobró), no Capa A (el alcance se
    /// evalúa dinámico por sucursal×canal, `[Decisión 12-2]`). Idempotente
    /// para la misma caja; re-asignar a otra caja es un error (el cobro
    /// vigente es único por comprobante).
    /// </summary>
    public void AsignarCajaCobro(Guid cajaId)
    {
        if (cajaId == Guid.Empty)
            throw new BusinessRuleException("COMPROBANTE_CAJA_INVALIDA", "La caja del cobro es obligatoria.");
        if (CajaId is Guid actual && actual != cajaId)
            throw new BusinessRuleException(
                "COMPROBANTE_CAJA_YA_ASIGNADA",
                "El comprobante ya tiene caja de cobro asignada; cancela el cobro vigente antes de re-cobrar.");
        CajaId = cajaId;
    }

    /// <summary>Desasigna la caja al cancelarse el cobro (permite re-cobrar en otra sesión).</summary>
    public void DesasignarCajaCobro() => CajaId = null;

    /// <summary>
    /// Snapshot del emisor congelado en este comprobante (F12-PR1). Lo usan los
    /// flujos derivados (NC de una factura, REPP, siguiente tramo de Carta
    /// Porte) para heredar el emisor del comprobante origen sin re-consultar el
    /// master. En comprobantes históricos pre-F12 Nombre/LugarExpedicion vienen
    /// vacíos y <see cref="DatosFiscalesEmisor.Validar"/> lo rechaza.
    /// </summary>
    public DatosFiscalesEmisor SnapshotEmisor() =>
        new(RfcEmisor, NombreEmisor, RegimenFiscalEmisor, LugarExpedicion);

    /// <summary>
    /// Agrega una relación CFDI a otro comprobante por su UUID. Idempotente por
    /// (uuid, tipo). Debe llamarse antes de timbrar (la relación va en el XML, que
    /// es inmutable una vez timbrado).
    /// </summary>
    public void AgregarRelacion(string uuidRelacionado, string tipoRelacion)
    {
        if (string.IsNullOrWhiteSpace(uuidRelacionado))
            throw new BusinessRuleException("RELACION_UUID_INVALIDO", "El UUID relacionado es obligatorio.");
        if (string.IsNullOrWhiteSpace(tipoRelacion))
            throw new BusinessRuleException("RELACION_TIPO_INVALIDO", "El tipo de relación es obligatorio.");
        if (_relaciones.Any(r => r.UuidRelacionado == uuidRelacionado && r.TipoRelacion == tipoRelacion))
            return;

        _relaciones.Add(new RelacionCfdi(Guid.CreateVersion7(), Id, uuidRelacionado, tipoRelacion));
    }

    /// <summary>
    /// Transición <c>Timbrado → CancelacionPendiente</c>: se solicitó la
    /// cancelación SAT 4.0 y se espera la resolución (aceptación del receptor o
    /// del PAC). Asíncrono-tolerante (§4.4).
    /// </summary>
    public void MarcarCancelacionPendiente()
    {
        if (Estado != EstadoTimbrado.Timbrado)
            throw new BusinessRuleException(
                "COMPROBANTE_NO_CANCELABLE",
                $"Solo un comprobante Timbrado puede solicitar cancelación (actual: {Estado}).");

        Estado = EstadoTimbrado.CancelacionPendiente;
    }

    /// <summary>Transición <c>CancelacionPendiente → Cancelado</c>: el SAT aceptó la cancelación.</summary>
    public void MarcarCancelado()
    {
        if (Estado != EstadoTimbrado.CancelacionPendiente)
            throw new BusinessRuleException(
                "COMPROBANTE_NO_EN_CANCELACION",
                $"Solo un comprobante en CancelacionPendiente puede marcarse Cancelado (actual: {Estado}).");

        Estado = EstadoTimbrado.Cancelado;
    }

    /// <summary>
    /// Transición <c>CancelacionPendiente → Timbrado</c>: el SAT rechazó la
    /// cancelación; el CFDI sigue vigente.
    /// </summary>
    public void RevertirCancelacion()
    {
        if (Estado != EstadoTimbrado.CancelacionPendiente)
            throw new BusinessRuleException(
                "COMPROBANTE_NO_EN_CANCELACION",
                $"Solo un comprobante en CancelacionPendiente puede revertir la cancelación (actual: {Estado}).");

        Estado = EstadoTimbrado.Timbrado;
    }

    /// <summary>
    /// Transición <c>Borrador → PendientePedimento</c> (uso del subtipo
    /// <c>FacturaVenta</c>, §3.bis.5). La validación de líneas la hace el subtipo.
    /// </summary>
    protected void PasarAPendientePedimento()
    {
        if (Estado != EstadoTimbrado.Borrador)
            throw new BusinessRuleException("FACTURA_NO_BORRADOR", $"Solo un Borrador puede pasar a PendientePedimento (actual: {Estado}).");
        Estado = EstadoTimbrado.PendientePedimento;
    }

    /// <summary>Transición <c>PendientePedimento → Borrador</c> al aplicar el pedimento (§3.bis.5).</summary>
    protected void VolverABorradorDesdePedimento()
    {
        if (Estado != EstadoTimbrado.PendientePedimento)
            throw new BusinessRuleException("FACTURA_NO_PENDIENTE_PEDIMENTO", $"Solo PendientePedimento vuelve a Borrador (actual: {Estado}).");
        Estado = EstadoTimbrado.Borrador;
    }

    /// <summary>Establece los totales calculados desde las líneas del subtipo.</summary>
    protected void EstablecerTotales(
        decimal subtotal,
        decimal descuento,
        decimal impuestosTrasladados,
        decimal retenciones,
        decimal total)
    {
        Subtotal = subtotal;
        Descuento = descuento;
        ImpuestosTrasladados = impuestosTrasladados;
        Retenciones = retenciones;
        Total = total;
    }
}
