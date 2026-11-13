using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>
    /// M-235: firmas que pide la cancelación (1 o 2), según <see cref="ReglaFirmaCancelacion"/>
    /// al solicitarla. La última firma es la que la envía al PAC (<see cref="DecididaPorId"/>).
    /// </summary>
    public short FirmasRequeridas { get; private set; } = 1;

    /// <summary>M-235: importe del comprobante en MXN al solicitar (forma parte de la huella).</summary>
    public decimal ImporteMxn { get; private set; }

    /// <summary>M-235: primera de dos firmas; la solicitud sigue pendiente hasta la segunda.</summary>
    public Guid? PrimeraFirmaPorId { get; private set; }
    public string? PrimeraFirmaPorNombre { get; private set; }
    public DateTimeOffset? PrimeraFirmaEn { get; private set; }

    /// <summary>M-235: huella de la solicitud que vio quien dio la primera firma.</summary>
    public string? PrimeraFirmaHuella { get; private set; }

    /// <summary>M-235: la solicitud cambió después de autorizarse y su firma se anuló (se pide de nuevo).</summary>
    public string? FirmaAnuladaMotivo { get; private set; }

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
        Guid? solicitadaPorId = null, string? solicitadaPorNombre = null, decimal importeMxn = 0m, short firmasRequeridas = 1)
    {
        if (string.IsNullOrWhiteSpace(motivoSat))
            throw new BusinessRuleException("CANCELACION_MOTIVO_INVALIDO", "El motivo SAT de cancelación es obligatorio.");
        ValidarSustituto(motivoSat, uuidSustituto);
        if (firmasRequeridas is < 1 or > 2)
            throw new BusinessRuleException("CANCELACION_REGLA_FIRMAS_INVALIDAS", "Una cancelación pide una o dos firmas.");

        return new SolicitudCancelacion(
            Guid.CreateVersion7(), empresaId, comprobanteId, motivoSat, uuidSustituto, solicitadaPorId, solicitadaPorNombre, ahora)
        {
            ImporteMxn = importeMxn,
            FirmasRequeridas = firmasRequeridas,
        };
    }

    /// <summary>
    /// M-235: huella de lo que se autoriza (motivo SAT, UUID sustituto e importe en MXN). Quien
    /// autoriza manda la que vio; si la solicitud cambió, la ejecución se bloquea.
    /// </summary>
    public string Huella => CalcularHuella(MotivoSat, UuidSustituto, ImporteMxn);

    public static string CalcularHuella(string motivoSat, string? uuidSustituto, decimal importeMxn)
    {
        var texto = string.Join('|', motivoSat, (uuidSustituto ?? string.Empty).Trim().ToUpperInvariant(),
            importeMxn.ToString("0.00", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)))[..16].ToLowerInvariant();
    }

    /// <summary>Firmas ya dadas (0, 1 o 2) mientras la solicitud está pendiente o enviada.</summary>
    public int FirmasDadas => DecididaPorId is not null && Estado is not EstadoSolicitudCancelacion.RechazadaPorAutorizador
        ? FirmasRequeridas
        : PrimeraFirmaPorId is not null ? 1 : 0;

    /// <summary>Solicitud aún no resuelta (pendiente de autorizar o ya enviada al PAC).</summary>
    public bool EstaViva => Estado is EstadoSolicitudCancelacion.PendienteAutorizacion
        or EstadoSolicitudCancelacion.Solicitada or EstadoSolicitudCancelacion.EnProceso;

    /// <summary>
    /// PendienteAutorizacion → Solicitada: el Contador General la autoriza y queda lista
    /// para enviarse al PAC. Quien la solicitó no puede autorizarla.
    /// </summary>
    /// <returns>
    /// M-235: <c>true</c> si con esta firma se completan las requeridas y la solicitud queda
    /// lista para el PAC; <c>false</c> si fue la primera de dos (sigue pendiente).
    /// </returns>
    public bool Autorizar(Guid usuarioId, string? usuarioNombre, DateTimeOffset ahora, string? huellaVista = null)
    {
        ExigirPendiente();
        ExigirOtraPersona(usuarioId);
        ValidarSustituto(MotivoSat, UuidSustituto);
        if (huellaVista is not null && !string.Equals(huellaVista, Huella, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("CANCELACION_VERSION_CAMBIO",
                "La solicitud cambió desde que la abriste (motivo, sustituto o importe). Revísala y vuelve a autorizar.");
        if (PrimeraFirmaPorId is not null && PrimeraFirmaHuella != Huella)
            throw new BusinessRuleException("CANCELACION_FIRMA_VENCIDA",
                "La solicitud cambió después de la primera firma; la firma se anuló y se pide de nuevo.");

        if (FirmasRequeridas >= 2 && PrimeraFirmaPorId is null)
        {
            PrimeraFirmaPorId = usuarioId;
            PrimeraFirmaPorNombre = usuarioNombre;
            PrimeraFirmaEn = ahora;
            PrimeraFirmaHuella = Huella;
            FirmaAnuladaMotivo = null;
            return false;
        }
        if (PrimeraFirmaPorId == usuarioId)
            throw new BusinessRuleException("CANCELACION_FIRMA_MISMA_PERSONA",
                "Quien dio la primera firma no puede dar también la segunda; firma otra persona.");

        Estado = EstadoSolicitudCancelacion.Solicitada;
        DecididaPorId = usuarioId;
        DecididaPorNombre = usuarioNombre;
        DecididaEn = ahora;
        return true;
    }

    /// <summary>
    /// M-235: antes de ejecutar se compara el importe actual del comprobante con el autorizado.
    /// Si cambió, la solicitud toma el importe nuevo y la primera firma (si la había) se anula:
    /// hay que volver a autorizar. Devuelve <c>true</c> si la solicitud cambió.
    /// </summary>
    public bool ActualizarImporte(decimal importeMxnActual)
    {
        if (Estado != EstadoSolicitudCancelacion.PendienteAutorizacion || importeMxnActual == ImporteMxn) return false;
        var sinCapturar = ImporteMxn == 0m && PrimeraFirmaPorId is null; // solicitudes anteriores a M-235
        ImporteMxn = importeMxnActual;
        if (sinCapturar) return false;
        if (PrimeraFirmaPorId is not null)
            AnularPrimeraFirma("Cambió el importe del comprobante después de la primera firma.");
        return true;
    }

    /// <summary>
    /// M-235: quien la solicitó corrige el motivo SAT o el sustituto mientras sigue pendiente.
    /// Cualquier firma previa se anula: hay que autorizar otra vez.
    /// </summary>
    public void Modificar(Guid usuarioId, string motivoSat, string? uuidSustituto)
    {
        ExigirPendiente();
        if (SolicitadaPorId is not null && SolicitadaPorId != usuarioId)
            throw new BusinessRuleException("CANCELACION_MODIFICAR_SOLO_SOLICITANTE",
                "Solo quien solicitó la cancelación puede modificarla.");
        if (string.IsNullOrWhiteSpace(motivoSat))
            throw new BusinessRuleException("CANCELACION_MOTIVO_INVALIDO", "El motivo SAT de cancelación es obligatorio.");
        var sustituto = motivoSat == "01" ? uuidSustituto?.Trim() : null;
        ValidarSustituto(motivoSat, sustituto);
        if (motivoSat == MotivoSat && sustituto == UuidSustituto) return;

        MotivoSat = motivoSat;
        UuidSustituto = sustituto;
        if (PrimeraFirmaPorId is not null)
            AnularPrimeraFirma("Se modificó el motivo o el sustituto después de la primera firma.");
    }

    private void AnularPrimeraFirma(string motivo)
    {
        PrimeraFirmaPorId = null;
        PrimeraFirmaPorNombre = null;
        PrimeraFirmaEn = null;
        PrimeraFirmaHuella = null;
        FirmaAnuladaMotivo = motivo;
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

    /// <summary>
    /// H-U31: el SAT aún no refleja el CFDI (205). La solicitud sigue <c>Solicitada</c> (misma solicitud, sin otra ni otro
    /// folio) con el aviso de cuándo reintentar; el reintento la envía de nuevo al PAC.
    /// </summary>
    public void MarcarPendienteReintentoSat(string aviso)
    {
        if (Estado != EstadoSolicitudCancelacion.Solicitada)
            throw new BusinessRuleException("CANCELACION_TRANSICION_INVALIDA", $"No se puede dejar pendiente de reintento desde {Estado}.");
        MensajeError = aviso;
    }

    /// <summary>H-U31: solicitada y en espera de que el SAT refleje el CFDI.</summary>
    public bool PendienteReintentoSat(string prefijoAviso) =>
        Estado == EstadoSolicitudCancelacion.Solicitada
        && MensajeError is not null && MensajeError.StartsWith(prefijoAviso, StringComparison.Ordinal);

    /// <summary>H-U31: al reenviar, el aviso anterior deja de aplicar.</summary>
    public void LimpiarAvisoReintento() => MensajeError = null;

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
