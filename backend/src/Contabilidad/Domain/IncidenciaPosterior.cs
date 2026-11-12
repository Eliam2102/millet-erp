using System.Text.Json.Serialization;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoIncidenciaPosterior : short { Abierta = 0, Resuelta = 1 }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResolucionIncidencia : short { PolizaAjuste = 1, ReaperturaAcotada = 2 }

/// <summary>
/// M-321 (86e4084j7) · incidencia posterior: algo que se encontró después de autorizar una póliza o de cerrar un periodo (qué se
/// encontró, quién lo registró, impacto). Mientras está abierta aparece como pendiente en el cierre del periodo afectado; se resuelve
/// al ligarla a su corrección: la póliza de ajuste o la reapertura acotada del periodo (M-213).
/// </summary>
public sealed class IncidenciaPosterior : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int HallazgoMinimo = 5, HallazgoMaximo = 1000, ImpactoMinimo = 3, ImpactoMaximo = 500, NotaMaximo = 500;

    public Guid EmpresaId { get; set; }
    /// <summary>Póliza autorizada a la que se liga (opcional si la incidencia es del periodo cerrado en general).</summary>
    public Guid? PolizaId { get; private set; }
    public int PeriodoAnio { get; private set; }
    public int PeriodoNumero { get; private set; }
    public string Hallazgo { get; private set; } = string.Empty;
    public string Impacto { get; private set; } = string.Empty;
    public decimal? ImporteImpacto { get; private set; }
    public EstadoIncidenciaPosterior Estado { get; private set; }
    public Guid? RegistradaPorId { get; private set; }
    public string RegistradaPor { get; private set; } = string.Empty;
    public DateTimeOffset RegistradaEn { get; private set; }
    public ResolucionIncidencia? Resolucion { get; private set; }
    public Guid? PolizaAjusteId { get; private set; }
    public Guid? ReaperturaId { get; private set; }
    public string? NotaResolucion { get; private set; }
    public Guid? ResueltaPorId { get; private set; }
    public string? ResueltaPor { get; private set; }
    public DateTimeOffset? ResueltaEn { get; private set; }

    private IncidenciaPosterior() { }

    public IncidenciaPosterior(Guid id, Guid? polizaId, int periodoAnio, int periodoNumero, string? hallazgo, string? impacto, decimal? importeImpacto,
        Guid? usuarioId, string usuarioNombre, DateTimeOffset ahora) : base(id)
    {
        if (hallazgo is null || hallazgo.Trim().Length is < HallazgoMinimo or > HallazgoMaximo)
            throw new BusinessRuleException("CONTAB_INCIDENCIA_HALLAZGO_INVALIDO",
                $"Describa qué se encontró (entre {HallazgoMinimo} y {HallazgoMaximo} caracteres).");
        if (impacto is null || impacto.Trim().Length is < ImpactoMinimo or > ImpactoMaximo)
            throw new BusinessRuleException("CONTAB_INCIDENCIA_IMPACTO_INVALIDO",
                $"Describa el impacto (entre {ImpactoMinimo} y {ImpactoMaximo} caracteres).");
        if (importeImpacto is < 0m)
            throw new BusinessRuleException("CONTAB_INCIDENCIA_IMPORTE_INVALIDO", "El importe del impacto no puede ser negativo.");
        PolizaId = polizaId;
        PeriodoAnio = periodoAnio;
        PeriodoNumero = periodoNumero;
        Hallazgo = hallazgo.Trim();
        Impacto = impacto.Trim();
        ImporteImpacto = importeImpacto;
        Estado = EstadoIncidenciaPosterior.Abierta;
        RegistradaPorId = usuarioId;
        RegistradaPor = usuarioNombre;
        RegistradaEn = ahora;
    }

    public bool Abierta => Estado == EstadoIncidenciaPosterior.Abierta;

    public void ResolverConPolizaAjuste(Guid polizaAjusteId, string? nota, Guid? usuarioId, string usuarioNombre, DateTimeOffset ahora)
    {
        ExigirAbierta();
        if (polizaAjusteId == PolizaId)
            throw new BusinessRuleException("CONTAB_INCIDENCIA_AJUSTE_ES_LA_MISMA", "La póliza de ajuste debe ser distinta de la póliza de la incidencia.");
        PolizaAjusteId = polizaAjusteId;
        Cerrar(ResolucionIncidencia.PolizaAjuste, nota, usuarioId, usuarioNombre, ahora);
    }

    public void ResolverConReapertura(Guid reaperturaId, string? nota, Guid? usuarioId, string usuarioNombre, DateTimeOffset ahora)
    {
        ExigirAbierta();
        ReaperturaId = reaperturaId;
        Cerrar(ResolucionIncidencia.ReaperturaAcotada, nota, usuarioId, usuarioNombre, ahora);
    }

    private void ExigirAbierta()
    {
        if (!Abierta) throw new ConflictException("CONTAB_INCIDENCIA_YA_RESUELTA", "La incidencia ya está resuelta.");
    }

    private void Cerrar(ResolucionIncidencia resolucion, string? nota, Guid? usuarioId, string usuarioNombre, DateTimeOffset ahora)
    {
        if (nota is { } n && n.Trim().Length > NotaMaximo)
            throw new BusinessRuleException("CONTAB_INCIDENCIA_NOTA_INVALIDA", $"La nota admite hasta {NotaMaximo} caracteres.");
        Estado = EstadoIncidenciaPosterior.Resuelta;
        Resolucion = resolucion;
        NotaResolucion = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim();
        ResueltaPorId = usuarioId;
        ResueltaPor = usuarioNombre;
        ResueltaEn = ahora;
    }
}
