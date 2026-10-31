using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Domain.Cancelaciones;

/// <summary>
/// Solicitud de cancelación SAT 4.0 de un <see cref="Comprobantes.Comprobante"/>
/// (§4.5 levantamiento). Rastrea el motivo SAT, el UUID sustituto (motivo 01) y
/// la FSM de resolución. F6.5: nace <see cref="EstadoSolicitudCancelacion.PendienteAutorizacion"/>;
/// solo al autorizarla el Contador General (otra persona que quien la solicitó) se
/// envía al PAC. La transición del comprobante a <c>Cancelado</c> la hace el
/// handler/worker al resolverse esta solicitud.
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

    /// <summary>Usuario que la solicitó (null en solicitudes anteriores a F6.5).</summary>
    public Guid? SolicitadaPorId { get; private set; }
    public string? SolicitadaPorNombre { get; private set; }

    /// <summary>Contador General que la autorizó o no la autorizó.</summary>
    public Guid? DecididaPorId { get; private set; }
    public string? DecididaPorNombre { get; private set; }
    public DateTimeOffset? DecididaEn { get; private set; }

    /// <summary>Motivo por el que el Contador General no la autorizó.</summary>
    public string? MotivoRechazo { get; private set; }

    private SolicitudCancelacion() { }

    private SolicitudCancelacion(
        Guid id, Guid empresaId, Guid comprobanteId, string motivoSat, string? uuidSustituto,
        Guid? solicitadaPorId, string? solicitadaPorNombre, DateTimeOffset ahora)
        : base(id)
    {
        EmpresaId = empresaId;
        ComprobanteId = comprobanteId;
        MotivoSat = motivoSat;
        UuidSustituto = uuidSustituto;
        SolicitadaPorId = solicitadaPorId;
        SolicitadaPorNombre = solicitadaPorNombre;
        Estado = EstadoSolicitudCancelacion.PendienteAutorizacion;
        SolicitadaEn = ahora;
    }

    /// <summary>
    /// Crea la solicitud pendiente de autorización. El motivo 01 (sustitución) exige
    /// <paramref name="uuidSustituto"/>.
    /// </summary>
    public static SolicitudCancelacion Crear(
        Guid empresaId, Guid comprobanteId, string motivoSat, string? uuidSustituto, DateTimeOffset ahora,
        Guid? solicitadaPorId = null, string? solicitadaPorNombre = null)
    {
        if (string.IsNullOrWhiteSpace(motivoSat))
            throw new BusinessRuleException("CANCELACION_MOTIVO_INVALIDO", "El motivo SAT de cancelación es obligatorio.");
        ValidarSustituto(motivoSat, uuidSustituto);

        return new SolicitudCancelacion(
            Guid.CreateVersion7(), empresaId, comprobanteId, motivoSat, uuidSustituto, solicitadaPorId, solicitadaPorNombre, ahora);
    }

    /// <summary>Solicitud aún no resuelta (pendiente de autorizar o ya enviada al PAC).</summary>
    public bool EstaViva => Estado is EstadoSolicitudCancelacion.PendienteAutorizacion
        or EstadoSolicitudCancelacion.Solicitada or EstadoSolicitudCancelacion.EnProceso;

    /// <summary>
    /// PendienteAutorizacion → Solicitada: el Contador General la autoriza y queda lista
    /// para enviarse al PAC. Quien la solicitó no puede autorizarla.
    /// </summary>
    public void Autorizar(Guid usuarioId, string? usuarioNombre, DateTimeOffset ahora)
    {
        ExigirPendiente();
        ExigirOtraPersona(usuarioId);
        ValidarSustituto(MotivoSat, UuidSustituto);
        Estado = EstadoSolicitudCancelacion.Solicitada;
        DecididaPorId = usuarioId;
        DecididaPorNombre = usuarioNombre;
        DecididaEn = ahora;
    }

    /// <summary>PendienteAutorizacion → RechazadaPorAutorizador, con motivo: el comprobante sigue vigente.</summary>
    public void RechazarAutorizacion(Guid usuarioId, string? usuarioNombre, string motivo, DateTimeOffset ahora)
    {
        ExigirPendiente();
        ExigirOtraPersona(usuarioId);
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("CANCELACION_MOTIVO_RECHAZO_REQUERIDO", "Indica el motivo por el que no se autoriza la cancelación.");
        Estado = EstadoSolicitudCancelacion.RechazadaPorAutorizador;
        MotivoRechazo = motivo.Trim();
        DecididaPorId = usuarioId;
        DecididaPorNombre = usuarioNombre;
        DecididaEn = ahora;
        ResueltaEn = ahora;
    }

    /// <summary>PendienteAutorizacion → Retirada: Ingresos la retira antes de que se autorice.</summary>
    public void Retirar(DateTimeOffset ahora)
    {
        ExigirPendiente();
        Estado = EstadoSolicitudCancelacion.Retirada;
        ResueltaEn = ahora;
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
        ExigirEnviada();
        Estado = EstadoSolicitudCancelacion.Aceptada;
        EstatusSat = estatusSat;
        ResueltaEn = ahora;
    }

    /// <summary>{Solicitada|EnProceso} → Rechazada: el SAT/receptor rechazó; el comprobante sigue vigente.</summary>
    public void MarcarRechazada(string? mensajeError, DateTimeOffset ahora)
    {
        ExigirEnviada();
        Estado = EstadoSolicitudCancelacion.Rechazada;
        MensajeError = mensajeError;
        ResueltaEn = ahora;
    }

    /// <summary>EnProceso → Vencida: sin respuesta del receptor en plazo (aceptación tácita).</summary>
    public void MarcarVencida(DateTimeOffset ahora)
    {
        ExigirEnviada();
        Estado = EstadoSolicitudCancelacion.Vencida;
        ResueltaEn = ahora;
    }

    private static void ValidarSustituto(string motivoSat, string? uuidSustituto)
    {
        if (motivoSat == "01" && string.IsNullOrWhiteSpace(uuidSustituto))
            throw new BusinessRuleException(
                "CANCELACION_SUSTITUTO_REQUERIDO",
                "El motivo 01 (con sustitución) exige el UUID del CFDI sustituto.");
    }

    private void ExigirPendiente()
    {
        if (Estado != EstadoSolicitudCancelacion.PendienteAutorizacion)
            throw new BusinessRuleException(
                "CANCELACION_NO_PENDIENTE", $"La solicitud ya no está pendiente de autorización ({Estado}).");
    }

    private void ExigirOtraPersona(Guid usuarioId)
    {
        if (SolicitadaPorId == usuarioId)
            throw new BusinessRuleException(
                "CANCELACION_AUTOAUTORIZACION", "Quien solicita la cancelación no puede autorizarla ni rechazarla.");
    }

    private void ExigirEnviada()
    {
        if (Estado is EstadoSolicitudCancelacion.Solicitada or EstadoSolicitudCancelacion.EnProceso) return;
        if (Estado is EstadoSolicitudCancelacion.PendienteAutorizacion)
            throw new BusinessRuleException("CANCELACION_TRANSICION_INVALIDA", "La solicitud aún no está autorizada.");
        throw new BusinessRuleException("CANCELACION_YA_RESUELTA", $"La solicitud ya está resuelta ({Estado}).");
    }
}
