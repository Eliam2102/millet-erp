using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

/// <summary>R27/D14: propuesta inmutable y resolución por una persona distinta de quien la preparó.</summary>
public sealed class SolicitudCatalogo : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public string Operacion { get; private set; } = string.Empty;
    public string ComandoJson { get; private set; } = string.Empty;
    public string? HuellaImportacion { get; private set; }
    public string? CodigoAlta { get; private set; }
    public string HuellaCatalogo { get; private set; } = string.Empty;
    public string CambiosJson { get; private set; } = string.Empty;
    public string Estado { get; private set; } = "Pendiente";
    public Guid PreparadaPorId { get; private set; }
    public string PreparadaPor { get; private set; } = string.Empty;
    public DateTimeOffset PreparadaEn { get; private set; }
    public Guid? ResueltaPorId { get; private set; }
    public string? ResueltaPor { get; private set; }
    public DateTimeOffset? ResueltaEn { get; private set; }
    public string? MotivoRechazo { get; private set; }

    private SolicitudCatalogo() { }
    public SolicitudCatalogo(Guid id, string operacion, string comandoJson, string huellaCatalogo,
        string cambiosJson, Guid preparadaPorId, string preparadaPor, DateTimeOffset preparadaEn, string? huellaImportacion = null,
        string? codigoAlta = null) : base(id)
    {
        if (preparadaPorId == Guid.Empty) throw new BusinessRuleException("CONTAB_ACTOR_REQUERIDO", "No se pudo identificar al usuario que prepara la solicitud.");
        HuellaImportacion = huellaImportacion;
        CodigoAlta = codigoAlta;
        Operacion = operacion;
        ComandoJson = comandoJson;
        HuellaCatalogo = huellaCatalogo;
        CambiosJson = cambiosJson;
        PreparadaPorId = preparadaPorId;
        PreparadaPor = preparadaPor;
        PreparadaEn = preparadaEn;
    }

    public void Resolver(Guid actorId, string actor, DateTimeOffset ahora, bool autorizar, string? motivo)
    {
        if (Estado != "Pendiente") throw new BusinessRuleException("CONTAB_SOLICITUD_RESUELTA", "La solicitud ya fue resuelta.");
        if (actorId == Guid.Empty) throw new BusinessRuleException("CONTAB_ACTOR_REQUERIDO", "No se pudo identificar al usuario que resuelve la solicitud.");
        if (autorizar && actorId == PreparadaPorId) throw new BusinessRuleException("CONTAB_SOLICITUD_MISMO_AUTOR",
            "Quien preparó la solicitud no puede autorizarla, aunque sea superadministrador. Debe resolverla otra persona con permiso de autorización del DAF.");
        if (!autorizar && (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > 1000))
            throw new BusinessRuleException("CONTAB_RECHAZO_MOTIVO_REQUERIDO", "Indique el motivo del rechazo (máximo 1000 caracteres).");
        Estado = autorizar ? "Autorizada" : "Rechazada";
        ResueltaPorId = actorId;
        ResueltaPor = actor;
        ResueltaEn = ahora;
        MotivoRechazo = autorizar ? null : motivo!.Trim();
    }
}
