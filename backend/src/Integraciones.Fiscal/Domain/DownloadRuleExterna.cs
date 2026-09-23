using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Integraciones.Fiscal.Domain;

/// <summary>
/// Cache local del lado Millet de una <c>DownloadRule</c> creada en
/// FiscalAPI. Las rules son plantillas (combinación de
/// <c>downloadType × satQueryType × satInvoiceStatus × personId</c>) que
/// se crean una sola vez por <see cref="RfcReceptor"/> y se reutilizan
/// para emitir N <see cref="SolicitudDescarga"/> contra ellas.
///
/// <para>
/// <b>Por qué guardar el ID externo localmente</b>: el SDK FiscalAPI no
/// expone una operación "encontrar mi rule por (personId, type)" — solo
/// listar y filtrar manualmente. Guardar el <see cref="RuleIdExterno"/>
/// evita llamadas extra y permite reuse idempotente del Submitter.
/// </para>
///
/// <para>
/// <b>Constraint único:</b> <c>(rfc_receptor_id, sat_query_type,
/// download_type, sat_invoice_status)</c> — una rule por combinación. Si
/// el operador cambia los flags, se desactiva la vieja y se crea otra.
/// </para>
///
/// <para>
/// Marcada <see cref="INotAudited"/>: es explícitamente una cache local
/// (ADR-0008 Capa 4 excluye "cache temporales") — el dato de negocio real
/// vive en FiscalAPI; esta fila solo evita llamadas repetidas al SDK.
/// </para>
/// </summary>
public sealed class DownloadRuleExterna : BaseEntity, IPerteneceAEmpresa, INotAudited
{
    public Guid EmpresaId { get; set; }
    public Guid RfcReceptorId { get; private set; }

    /// <summary>UUID de la rule en FiscalAPI (campo <c>id</c> del response).</summary>
    public string RuleIdExterno { get; private set; } = string.Empty;

    public SatQueryType SatQueryType { get; private set; }
    public DownloadType DownloadType { get; private set; }
    public SatInvoiceStatusFilter SatInvoiceStatus { get; private set; }

    /// <summary>
    /// Si <c>false</c>, el Submitter NO crea nuevas solicitudes contra
    /// esta rule. Las solicitudes ya emitidas continúan su ciclo de vida.
    /// </summary>
    public bool Activa { get; private set; }

    private DownloadRuleExterna() { } // EF Core

    public DownloadRuleExterna(
        Guid id,
        Guid empresaId,
        Guid rfcReceptorId,
        string ruleIdExterno,
        SatQueryType satQueryType,
        DownloadType downloadType,
        SatInvoiceStatusFilter satInvoiceStatus) : base(id)
    {
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("DOWNLOAD_RULE_EMPRESA_INVALIDA",
                "EmpresaId es requerida.");
        if (rfcReceptorId == Guid.Empty)
            throw new BusinessRuleException("DOWNLOAD_RULE_RFC_INVALIDO",
                "RfcReceptorId es requerido.");
        if (string.IsNullOrWhiteSpace(ruleIdExterno))
            throw new BusinessRuleException("DOWNLOAD_RULE_ID_EXTERNO_INVALIDO",
                "RuleIdExterno es requerido (viene del response de FiscalAPI).");

        EmpresaId = empresaId;
        RfcReceptorId = rfcReceptorId;
        RuleIdExterno = ruleIdExterno.Trim();
        SatQueryType = satQueryType;
        DownloadType = downloadType;
        SatInvoiceStatus = satInvoiceStatus;
        Activa = true;
    }

    public void Activar() => Activa = true;
    public void Desactivar() => Activa = false;
}
