using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Domain.Cancelaciones;

/// <summary>
/// Solicitud de cancelación SAT 4.0 de un <see cref="Comprobantes.Comprobante"/>
/// (§4.5 levantamiento). Rastrea el motivo SAT, el UUID sustituto (motivo 01) y
/// la FSM de resolución. La transición del comprobante a <c>Cancelado</c> la hace
/// el handler/worker al resolverse esta solicitud.
/// </summary>
public sealed class SolicitudCancelacion : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public Guid ComprobanteId { get; private set; }

    /// <summary>Clave SAT del motivo de cancelación: 01 (con sustitución), 02, 03, 04.</summary>
    public string MotivoSat { get; private set; } = string.Empty;

    /// <summary>UUID del CFDI que sustituye (obligatorio si <see cref="MotivoSat"/> = 01).</summary>
    public string? UuidSustituto { get; private set; }

    public EstadoSolicitudCancelacion Estado { get; private set; }

    /// <summary>Estatus reportado por el SAT (última consulta).</summary>
    public string? EstatusSat { get; private set; }

    public string? MensajeError { get; private set; }

    public DateTimeOffset SolicitadaEn { get; private set; }
    public DateTimeOffset? ResueltaEn { get; private set; }

    private SolicitudCancelacion() { }

    private SolicitudCancelacion(
        Guid id, Guid empresaId, Guid comprobanteId, string motivoSat, string? uuidSustituto, DateTimeOffset ahora)
        : base(id)
    {
        EmpresaId = empresaId;
        ComprobanteId = comprobanteId;
        MotivoSat = motivoSat;
        UuidSustituto = uuidSustituto;
        Estado = EstadoSolicitudCancelacion.Solicitada;
        SolicitadaEn = ahora;
    }

    /// <summary>
    /// Crea la solicitud. El motivo 01 (sustitución) exige <paramref name="uuidSustituto"/>.
    /// </summary>
    public static SolicitudCancelacion Crear(
        Guid empresaId, Guid comprobanteId, string motivoSat, string? uuidSustituto, DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(motivoSat))
            throw new BusinessRuleException("CANCELACION_MOTIVO_INVALIDO", "El motivo SAT de cancelación es obligatorio.");
        if (motivoSat == "01" && string.IsNullOrWhiteSpace(uuidSustituto))
            throw new BusinessRuleException(
                "CANCELACION_SUSTITUTO_REQUERIDO",
                "El motivo 01 (con sustitución) exige el UUID del CFDI sustituto.");

        return new SolicitudCancelacion(Guid.CreateVersion7(), empresaId, comprobanteId, motivoSat, uuidSustituto, ahora);
    }

    /// <summary>Solicitada → EnProceso: el PAC aceptó la solicitud; el SAT/receptor aún no resuelve.</summary>
    public void MarcarEnProceso(string? estatusSat)
    {
        if (Estado != EstadoSolicitudCancelacion.Solicitada)
            throw new BusinessRuleException("CANCELACION_TRANSICION_INVALIDA", $"No se puede pasar a EnProceso desde {Estado}.");
        Estado = EstadoSolicitudCancelacion.EnProceso;
        EstatusSat = estatusSat;
    }

    /// <summary>{Solicitada|EnProceso} → Aceptada: el SAT aceptó la cancelación.</summary>
    public void MarcarAceptada(string? estatusSat, DateTimeOffset ahora)
    {
        if (Estado is EstadoSolicitudCancelacion.Aceptada or EstadoSolicitudCancelacion.Rechazada or EstadoSolicitudCancelacion.Vencida)
            throw new BusinessRuleException("CANCELACION_YA_RESUELTA", $"La solicitud ya está resuelta ({Estado}).");
        Estado = EstadoSolicitudCancelacion.Aceptada;
        EstatusSat = estatusSat;
        ResueltaEn = ahora;
    }

    /// <summary>{Solicitada|EnProceso} → Rechazada: el SAT/receptor rechazó; el comprobante sigue vigente.</summary>
    public void MarcarRechazada(string? mensajeError, DateTimeOffset ahora)
    {
        if (Estado is EstadoSolicitudCancelacion.Aceptada or EstadoSolicitudCancelacion.Rechazada or EstadoSolicitudCancelacion.Vencida)
            throw new BusinessRuleException("CANCELACION_YA_RESUELTA", $"La solicitud ya está resuelta ({Estado}).");
        Estado = EstadoSolicitudCancelacion.Rechazada;
        MensajeError = mensajeError;
        ResueltaEn = ahora;
    }

    /// <summary>EnProceso → Vencida: sin respuesta del receptor en plazo (aceptación tácita).</summary>
    public void MarcarVencida(DateTimeOffset ahora)
    {
        if (Estado is EstadoSolicitudCancelacion.Aceptada or EstadoSolicitudCancelacion.Rechazada or EstadoSolicitudCancelacion.Vencida)
            throw new BusinessRuleException("CANCELACION_YA_RESUELTA", $"La solicitud ya está resuelta ({Estado}).");
        Estado = EstadoSolicitudCancelacion.Vencida;
        ResueltaEn = ahora;
    }
}
