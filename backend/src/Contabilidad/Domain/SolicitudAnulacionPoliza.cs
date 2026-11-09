using System.Text.Json.Serialization;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoSolicitudAnulacion : short { Pendiente = 0, Aprobada = 1, Rechazada = 2 }

/// <summary>
/// M-212 (86e3zr7pq): anular una póliza autorizada pide segunda firma. Una persona con el permiso de anular la solicita con motivo;
/// otra con el permiso de aprobar la aprueba (entonces se ejecuta la anulación K10.3 con ese motivo) o la rechaza con motivo. Mientras
/// está pendiente la póliza sigue autorizada. Solo una solicitud pendiente por póliza (índice único parcial). Patrón de la solicitud de
/// cancelación de Facturación (F6.5).
/// </summary>
public sealed class SolicitudAnulacionPoliza : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const string MensajeMismaPersona = "Otra persona con facultad debe aprobar la anulación.";

    public Guid EmpresaId { get; set; }
    public Guid PolizaId { get; private set; }
    public string Motivo { get; private set; } = string.Empty;

    /// <summary>Periodo abierto al que irá la anulación si el de la póliza ya no admite pólizas (el mismo dato del diálogo de anular).</summary>
    public int? PeriodoAnio { get; private set; }
    public int? PeriodoNumero { get; private set; }

    public EstadoSolicitudAnulacion Estado { get; private set; }
    public Guid? SolicitadaPorId { get; private set; }
    public string SolicitadaPor { get; private set; } = string.Empty;
    public DateTimeOffset SolicitadaEn { get; private set; }
    public Guid? DecididaPorId { get; private set; }
    public string? DecididaPor { get; private set; }
    public DateTimeOffset? DecididaEn { get; private set; }
    public string? MotivoRechazo { get; private set; }

    /// <summary>Póliza de anulación que nació al aprobar.</summary>
    public Guid? PolizaAnulacionId { get; private set; }

    private SolicitudAnulacionPoliza() { }

    public SolicitudAnulacionPoliza(Guid id, Guid polizaId, string motivo, int? periodoAnio, int? periodoNumero, Guid? usuarioId,
        string usuarioNombre, DateTimeOffset ahora) : base(id)
    {
        PolizaId = polizaId;
        Motivo = ValidarMotivo(motivo, "CONTAB_ANULACION_MOTIVO_INVALIDO", "de la anulación");
        PeriodoAnio = periodoAnio;
        PeriodoNumero = periodoNumero;
        Estado = EstadoSolicitudAnulacion.Pendiente;
        SolicitadaPorId = usuarioId;
        SolicitadaPor = usuarioNombre;
        SolicitadaEn = ahora;
    }

    public bool Pendiente => Estado == EstadoSolicitudAnulacion.Pendiente;

    /// <summary>Quien decide debe ser otra persona que quien solicitó (CA2), y la solicitud debe seguir pendiente.</summary>
    public void ExigirDecidible(Guid? usuarioId)
    {
        if (!Pendiente)
            throw new ConflictException("CONTAB_ANULACION_YA_DECIDIDA",
                $"La solicitud ya fue {(Estado == EstadoSolicitudAnulacion.Aprobada ? "aprobada" : "rechazada")}.");
        if (usuarioId is null || usuarioId == SolicitadaPorId)
            throw new BusinessRuleException("CONTAB_ANULACION_MISMA_PERSONA", MensajeMismaPersona);
    }

    /// <summary>Pendiente → Aprobada: la anulación ya se registró (<paramref name="polizaAnulacionId"/>).</summary>
    public void Aprobar(Guid? usuarioId, string usuarioNombre, Guid polizaAnulacionId, DateTimeOffset ahora)
    {
        ExigirDecidible(usuarioId);
        Estado = EstadoSolicitudAnulacion.Aprobada;
        DecididaPorId = usuarioId;
        DecididaPor = usuarioNombre;
        DecididaEn = ahora;
        PolizaAnulacionId = polizaAnulacionId;
    }

    /// <summary>Pendiente → Rechazada, con motivo: la póliza sigue autorizada y se puede pedir otra anulación (CA4).</summary>
    public void Rechazar(Guid? usuarioId, string usuarioNombre, string motivo, DateTimeOffset ahora)
    {
        ExigirDecidible(usuarioId);
        MotivoRechazo = ValidarMotivo(motivo, "CONTAB_ANULACION_MOTIVO_RECHAZO_INVALIDO", "del rechazo");
        Estado = EstadoSolicitudAnulacion.Rechazada;
        DecididaPorId = usuarioId;
        DecididaPor = usuarioNombre;
        DecididaEn = ahora;
    }

    private static string ValidarMotivo(string? motivo, string codigo, string de)
    {
        if (motivo is null || motivo.Trim().Length is < Poliza.MotivoMinimo or > Poliza.MotivoMaximo)
            throw new BusinessRuleException(codigo, $"Indique el motivo {de} (entre {Poliza.MotivoMinimo} y {Poliza.MotivoMaximo} caracteres).");
        return motivo.Trim();
    }
}
