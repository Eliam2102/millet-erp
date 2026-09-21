using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.Cfdi;

/// <summary>
/// Agregado raíz que representa un CFDI 4.0 que llegó al ERP. Ciclo de
/// vida independiente del pasivo asociado (§3.bis.1 del 01-diseno):
/// puede entrar por descarga SAT, mailbox o carga manual y nunca
/// convertirse en <c>FacturaProveedor</c> (descartado, duplicado).
///
/// <para>
/// Unicidad por <see cref="UuidCfdi"/> a nivel BD vía índice único.
/// La conversión a pasivo se hace en F3+ desde otro comando — este
/// agregado solo registra el estado <see cref="EstadoCfdiRecibido.ConvertidoEnPasivo"/>
/// y la FK al documento destino.
/// </para>
///
/// <para>
/// Las referencias a Blob storage (<see cref="XmlBlobRef"/>,
/// <see cref="PdfBlobRef"/>) son convenciones de path en Azure Blob /
/// filesystem local (ADR-0024): el binario vive en blob, esta tabla
/// solo guarda el path.
/// </para>
/// </summary>
public sealed class CfdiRecibido : BaseEntity, IPerteneceAEmpresa, IFiscalmenteRelevante, IAuditable
{
    public Guid EmpresaId { get; set; }

    public UuidCfdi UuidCfdi { get; private set; } = default!;

    public RfcMexicano RfcEmisor { get; private set; } = default!;
    public RfcMexicano RfcReceptor { get; private set; } = default!;

    public TipoCfdi Tipo { get; private set; }

    public string? Folio { get; private set; }
    public string? Serie { get; private set; }

    public DateTimeOffset FechaCfdi { get; private set; }

    public decimal Total { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal ImpuestosTrasladados { get; private set; }
    public decimal Retenciones { get; private set; }

    public string Moneda { get; private set; } = "MXN";
    public decimal? TipoCambio { get; private set; }

    /// <summary>
    /// MetodoPago SAT (PUE/PPD) del Comprobante (TES-PR8, [T-G11]). La
    /// captura de factura lo copia al pasivo y de ahí viaja a Tesorería en
    /// <c>pasivo.autorizado-para-pago.v1</c>. Nullable: filas históricas y
    /// XML sin atributo.
    /// </summary>
    public string? MetodoPago { get; private set; }

    public CanalOrigenCfdi CanalOrigen { get; private set; }
    public DateTimeOffset FechaRecepcion { get; private set; }

    public EstadoCfdiRecibido Estado { get; private set; }

    /// <summary>
    /// Path / blob reference al XML completo (ADR-0024). Convención:
    /// <c>cxp/{año}/{mes}/cfdi/{uuid}.xml</c>.
    /// <para>
    /// Marcada como nullable a nivel CLR por compatibilidad con filas
    /// históricas (PR-12 introdujo <c>MetadataOnly</c>; PR-14 lo eliminó
    /// — todo CFDI nuevo tiene XML). La factory <see cref="Ingresar"/>
    /// exige valor no vacío.
    /// </para>
    /// </summary>
    public string? XmlBlobRef { get; private set; }

    /// <summary>
    /// Path / blob reference al PDF si vino o se generó. NULL si no.
    /// Convención: <c>cxp/{año}/{mes}/cfdi/{uuid}.pdf</c>.
    /// </summary>
    public string? PdfBlobRef { get; private set; }

    /// <summary>
    /// Hash SHA-256 hex (lowercase) del XML original para deduplicación
    /// secundaria. Nullable a nivel CLR por compatibilidad histórica;
    /// la factory <see cref="Ingresar"/> lo exige no vacío.
    /// </summary>
    public string? XmlHashSha256 { get; private set; }

    // PR-12/PR-14 — Trazabilidad al feed de descarga masiva (auditoría).
    // Solo presentes cuando el canal de origen es DescargaSat; NULL en
    // mailbox/carga manual.
    public Guid? SolicitudDescargaId { get; private set; }
    public string? RequestIdExternoFiscalApi { get; private set; }

    /// <summary>FK al pasivo destino cuando <see cref="Estado"/> = <see cref="EstadoCfdiRecibido.ConvertidoEnPasivo"/>. Null en otros estados.</summary>
    public Guid? DocumentoDestinoId { get; private set; }

    /// <summary>Motivo del descarte cuando <see cref="Estado"/> = <see cref="EstadoCfdiRecibido.Descartado"/>.</summary>
    public string? MotivoDescarte { get; private set; }

    /// <summary>UUID del CFDI duplicado-de cuando <see cref="Estado"/> = <see cref="EstadoCfdiRecibido.Duplicado"/>.</summary>
    public Guid? CfdiOriginalId { get; private set; }

    private CfdiRecibido() { }

    private CfdiRecibido(
        Guid id,
        Guid empresaId,
        UuidCfdi uuid,
        RfcMexicano rfcEmisor,
        RfcMexicano rfcReceptor,
        TipoCfdi tipo,
        string? folio,
        string? serie,
        DateTimeOffset fechaCfdi,
        decimal total,
        decimal subtotal,
        decimal impuestosTrasladados,
        decimal retenciones,
        string moneda,
        decimal? tipoCambio,
        CanalOrigenCfdi canalOrigen,
        DateTimeOffset fechaRecepcion,
        string? xmlBlobRef,
        string? pdfBlobRef,
        string? xmlHashSha256,
        EstadoCfdiRecibido estado,
        Guid? solicitudDescargaId,
        string? requestIdExternoFiscalApi,
        string? metodoPago) : base(id)
    {
        EmpresaId = empresaId;
        UuidCfdi = uuid;
        RfcEmisor = rfcEmisor;
        RfcReceptor = rfcReceptor;
        Tipo = tipo;
        Folio = folio;
        Serie = serie;
        FechaCfdi = fechaCfdi;
        Total = total;
        Subtotal = subtotal;
        ImpuestosTrasladados = impuestosTrasladados;
        Retenciones = retenciones;
        Moneda = moneda;
        TipoCambio = tipoCambio;
        CanalOrigen = canalOrigen;
        FechaRecepcion = fechaRecepcion;
        XmlBlobRef = xmlBlobRef;
        PdfBlobRef = pdfBlobRef;
        XmlHashSha256 = xmlHashSha256;
        Estado = estado;
        SolicitudDescargaId = solicitudDescargaId;
        RequestIdExternoFiscalApi = requestIdExternoFiscalApi;
        MetodoPago = metodoPago;
    }

    /// <summary>
    /// Crea un CFDI en estado <see cref="EstadoCfdiRecibido.PorProcesar"/>.
    /// La unicidad por UUID se valida en la capa de aplicación con un
    /// query previo + el constraint único de BD como red final.
    ///
    /// <para>
    /// <paramref name="solicitudDescargaId"/> y
    /// <paramref name="requestIdExternoFiscalApi"/> son opcionales y solo
    /// se setean cuando el canal es <see cref="CanalOrigenCfdi.DescargaSat"/>
    /// — sirven como audit trail para correlacionar el CFDI con el
    /// download-request original (PR-14, doc 02 §13.10).
    /// </para>
    /// </summary>
    public static CfdiRecibido Ingresar(
        Guid empresaId,
        UuidCfdi uuid,
        RfcMexicano rfcEmisor,
        RfcMexicano rfcReceptor,
        TipoCfdi tipo,
        string? folio,
        string? serie,
        DateTimeOffset fechaCfdi,
        decimal total,
        decimal subtotal,
        decimal impuestosTrasladados,
        decimal retenciones,
        string moneda,
        decimal? tipoCambio,
        CanalOrigenCfdi canalOrigen,
        DateTimeOffset fechaRecepcion,
        string xmlBlobRef,
        string? pdfBlobRef,
        string xmlHashSha256,
        Guid? solicitudDescargaId = null,
        string? requestIdExternoFiscalApi = null,
        string? metodoPago = null)
    {
        if (total < 0)
            throw new BusinessRuleException("CFDI_TOTAL_NEGATIVO", "El total del CFDI no puede ser negativo.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("CFDI_MONEDA_INVALIDA", "La moneda debe ser un código ISO 4217 de 3 letras.");
        if (string.IsNullOrWhiteSpace(xmlBlobRef))
            throw new BusinessRuleException("CFDI_XML_BLOB_REF_VACIA", "La referencia al XML en blob es obligatoria.");
        if (string.IsNullOrWhiteSpace(xmlHashSha256))
            throw new BusinessRuleException("CFDI_HASH_VACIO", "El hash SHA-256 del XML es obligatorio.");

        return new CfdiRecibido(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            uuid: uuid,
            rfcEmisor: rfcEmisor,
            rfcReceptor: rfcReceptor,
            tipo: tipo,
            folio: folio,
            serie: serie,
            fechaCfdi: fechaCfdi,
            total: total,
            subtotal: subtotal,
            impuestosTrasladados: impuestosTrasladados,
            retenciones: retenciones,
            moneda: moneda.ToUpperInvariant(),
            tipoCambio: tipoCambio,
            canalOrigen: canalOrigen,
            fechaRecepcion: fechaRecepcion,
            xmlBlobRef: xmlBlobRef,
            pdfBlobRef: pdfBlobRef,
            xmlHashSha256: xmlHashSha256,
            estado: EstadoCfdiRecibido.PorProcesar,
            solicitudDescargaId: solicitudDescargaId,
            requestIdExternoFiscalApi: requestIdExternoFiscalApi,
            metodoPago: NormalizarMetodoPago(metodoPago));
    }

    /// <summary>Normaliza el MetodoPago SAT: trim + upper; vacío → null.</summary>
    private static string? NormalizarMetodoPago(string? metodoPago)
    {
        var v = metodoPago?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    /// <summary>
    /// Marca el CFDI como duplicado del referenciado por
    /// <paramref name="cfdiOriginalId"/>. Solo aplica si está en
    /// <see cref="EstadoCfdiRecibido.PorProcesar"/>.
    /// </summary>
    public void MarcarDuplicado(Guid cfdiOriginalId)
    {
        if (Estado != EstadoCfdiRecibido.PorProcesar)
        {
            throw new BusinessRuleException(
                "CFDI_ESTADO_INVALIDO",
                $"Solo un CFDI en estado PorProcesar puede marcarse como Duplicado (actual: {Estado}).");
        }

        if (cfdiOriginalId == Id)
        {
            throw new BusinessRuleException(
                "CFDI_AUTO_DUPLICADO",
                "Un CFDI no puede marcarse duplicado de sí mismo.");
        }

        Estado = EstadoCfdiRecibido.Duplicado;
        CfdiOriginalId = cfdiOriginalId;
    }

    /// <summary>
    /// Descarta el CFDI con motivo. Solo aplica si está en
    /// <see cref="EstadoCfdiRecibido.PorProcesar"/>.
    /// </summary>
    public void Descartar(string motivo)
    {
        if (Estado != EstadoCfdiRecibido.PorProcesar)
        {
            throw new BusinessRuleException(
                "CFDI_ESTADO_INVALIDO",
                $"Solo un CFDI en estado PorProcesar puede descartarse (actual: {Estado}).");
        }

        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new BusinessRuleException(
                "CFDI_MOTIVO_DESCARTE_VACIO",
                "El motivo del descarte es obligatorio.");
        }

        Estado = EstadoCfdiRecibido.Descartado;
        MotivoDescarte = motivo.Trim();
    }

    /// <summary>
    /// Marca el CFDI como convertido en pasivo (factura / NC / anticipo).
    /// Lo invocan los handlers de captura en F3+. Solo aplica desde
    /// <see cref="EstadoCfdiRecibido.PorProcesar"/>.
    /// </summary>
    public void MarcarConvertidoEnPasivo(Guid documentoDestinoId)
    {
        if (Estado != EstadoCfdiRecibido.PorProcesar)
        {
            throw new BusinessRuleException(
                "CFDI_ESTADO_INVALIDO",
                $"Solo un CFDI en estado PorProcesar puede convertirse en pasivo (actual: {Estado}).");
        }

        Estado = EstadoCfdiRecibido.ConvertidoEnPasivo;
        DocumentoDestinoId = documentoDestinoId;
    }

    /// <summary>
    /// Reversa de <see cref="MarcarConvertidoEnPasivo"/>: regresa el CFDI
    /// a <see cref="EstadoCfdiRecibido.PorProcesar"/> cuando el documento
    /// destino se compensa (comprobación de gastos rechazada, P7-H1). El
    /// caller debe cancelar el documento destino en la misma transacción.
    /// </summary>
    public void RevertirAPorProcesar(Guid documentoDestinoId)
    {
        if (Estado != EstadoCfdiRecibido.ConvertidoEnPasivo)
        {
            throw new BusinessRuleException(
                "CFDI_ESTADO_INVALIDO",
                $"Solo un CFDI ConvertidoEnPasivo puede revertirse a PorProcesar (actual: {Estado}).");
        }

        if (DocumentoDestinoId != documentoDestinoId)
        {
            throw new BusinessRuleException(
                "CFDI_DESTINO_NO_COINCIDE",
                "El CFDI está convertido en un documento distinto al que se compensa.");
        }

        Estado = EstadoCfdiRecibido.PorProcesar;
        DocumentoDestinoId = null;
    }
}
