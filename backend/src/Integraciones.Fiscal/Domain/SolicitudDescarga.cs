using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Integraciones.Fiscal.Domain;

/// <summary>
/// Instancia local de una <c>DownloadRequest</c> creada en FiscalAPI.
/// Lleva la FSM de la solicitud lado Millet (que es DISTINTA pero
/// mapeada a la FSM de FiscalAPI/SAT — ver §13.3 implicación 3 del doc
/// <c>02-flujo-asincrono.md</c>):
///
/// <code>
/// Pendiente (1) ──submit OK──▶ EsperandoSat (2) ──SAT 3──▶ EsperandoApi (3) ──API procesa──▶ Terminada (4)
///    │                                                                                          │
///    │                                                                                          │ cosecha
///    │                                                                                          ▼
///    │                                                                                       Cosechada (5)
///    │                                                                                          │
///    │                                                                                          │ adapter ack
///    │                                                                                          ▼
///    └── submit falla / SAT abandona / SAT error ──▶ Error (-1)                              Cerrada (6)
/// </code>
///
/// <para>
/// Mapeo FSM externa → interna (ver §13.3 catálogos):
/// </para>
/// <list type="bullet">
///   <item><c>satRequestStatusId=0/1/2</c> (Desconocido/Aceptada/En proceso) + <c>downloadRequestStatusId=1</c> → <see cref="EstadoSolicitudDescarga.EsperandoSat"/></item>
///   <item><c>satRequestStatusId=3</c> (Terminada) + <c>downloadRequestStatusId=2</c> (Esperando API) → <see cref="EstadoSolicitudDescarga.EsperandoApi"/></item>
///   <item><c>downloadRequestStatusId=3</c> (Completada) → <see cref="EstadoSolicitudDescarga.Terminada"/></item>
///   <item><c>satRequestStatusId in (4,5,6,-1)</c> (Error/Rechazada/Vencida/Abandonada) → <see cref="EstadoSolicitudDescarga.Error"/></item>
/// </list>
/// </summary>
public sealed class SolicitudDescarga : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid DownloadRuleId { get; private set; }

    /// <summary>UUID de la request en FiscalAPI (campo <c>id</c> del response).</summary>
    public string RequestIdExterno { get; private set; } = string.Empty;

    public DateTimeOffset StartDate { get; private set; }
    public DateTimeOffset EndDate { get; private set; }

    public EstadoSolicitudDescarga Estado { get; private set; }

    /// <summary>Copia del <c>satRequestStatusId</c> del último poll (0/1/2/3/4/5/6/-1).</summary>
    public int? SatRequestStatusExterno { get; private set; }

    /// <summary>Copia del <c>downloadRequestStatusId</c> del último poll (1/2/3/-1).</summary>
    public int? DownloadRequestStatusExterno { get; private set; }

    public int? InvoiceCount { get; private set; }
    public DateTimeOffset? LastPollAt { get; private set; }
    public DateTimeOffset NextPollAt { get; private set; }
    public DateTimeOffset? CosechadaAt { get; private set; }
    public DateTimeOffset? CerradaAt { get; private set; }
    public string? ErrorCodigo { get; private set; }
    public string? ErrorMensaje { get; private set; }
    public int AttemptsPoll { get; private set; }

    private SolicitudDescarga() { } // EF Core

    public SolicitudDescarga(
        Guid id,
        Guid empresaId,
        Guid downloadRuleId,
        string requestIdExterno,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        DateTimeOffset ahora) : base(id)
    {
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("SOLICITUD_DESCARGA_EMPRESA_INVALIDA",
                "EmpresaId es requerida.");
        if (downloadRuleId == Guid.Empty)
            throw new BusinessRuleException("SOLICITUD_DESCARGA_RULE_INVALIDA",
                "DownloadRuleId es requerida.");
        if (string.IsNullOrWhiteSpace(requestIdExterno))
            throw new BusinessRuleException("SOLICITUD_DESCARGA_REQUEST_EXTERNO_INVALIDO",
                "RequestIdExterno es requerido (viene del response de FiscalAPI).");
        if (endDate <= startDate)
            throw new BusinessRuleException("SOLICITUD_DESCARGA_VENTANA_INVALIDA",
                "endDate debe ser mayor a startDate.");

        EmpresaId = empresaId;
        DownloadRuleId = downloadRuleId;
        RequestIdExterno = requestIdExterno.Trim();
        StartDate = startDate;
        EndDate = endDate;
        Estado = EstadoSolicitudDescarga.EsperandoSat;
        NextPollAt = ahora;
        AttemptsPoll = 0;
    }

    /// <summary>
    /// Aplica el resultado de un poll a FiscalAPI. Mapea los estados
    /// externos (sat + api) al <see cref="EstadoSolicitudDescarga"/>
    /// interno y avanza la FSM. Calcula el siguiente <see cref="NextPollAt"/>
    /// con backoff exponencial — ver doc 02 §5.2.
    /// </summary>
    public void AplicarPoll(
        int? satStatusExterno,
        int? downloadRequestStatusExterno,
        int? invoiceCount,
        DateTimeOffset ahora,
        int maxBackoffMinutes = 360)
    {
        if (Estado is EstadoSolicitudDescarga.Cosechada
                   or EstadoSolicitudDescarga.Cerrada
                   or EstadoSolicitudDescarga.Error)
            return; // terminales — no se re-pollean

        AttemptsPoll++;
        LastPollAt = ahora;
        SatRequestStatusExterno = satStatusExterno;
        DownloadRequestStatusExterno = downloadRequestStatusExterno;
        InvoiceCount = invoiceCount;

        // Errores terminales del SAT (4/5/6/-1) o Abandonada en FiscalAPI (-1).
        if (satStatusExterno is 4 or 5 or 6 or -1
            || downloadRequestStatusExterno is -1)
        {
            Estado = EstadoSolicitudDescarga.Error;
            ErrorCodigo = $"SAT={satStatusExterno},REQ={downloadRequestStatusExterno}";
            ErrorMensaje = "Solicitud terminó en estado de error o abandono en SAT/FiscalAPI.";
            return;
        }

        // Completada (3) → Terminada interno; cosecha pendiente.
        if (downloadRequestStatusExterno is 3)
        {
            Estado = EstadoSolicitudDescarga.Terminada;
            return;
        }

        // Esperando API (2) → ya pasó por SAT; falta cosecha de FiscalAPI.
        if (downloadRequestStatusExterno is 2)
        {
            Estado = EstadoSolicitudDescarga.EsperandoApi;
        }
        else
        {
            Estado = EstadoSolicitudDescarga.EsperandoSat;
        }

        // Backoff exponencial: 15min * 2^attempts, cap maxBackoffMinutes.
        var backoffMinutes = Math.Min((int)(15 * Math.Pow(2, Math.Min(AttemptsPoll - 1, 6))), maxBackoffMinutes);
        NextPollAt = ahora.AddMinutes(backoffMinutes);
    }

    /// <summary>
    /// Marca la solicitud como cosechada (después de listar /meta-items).
    /// Solo válido desde <see cref="EstadoSolicitudDescarga.Terminada"/>.
    /// </summary>
    public void MarcarCosechada(DateTimeOffset ahora)
    {
        if (Estado != EstadoSolicitudDescarga.Terminada)
            throw new BusinessRuleException("SOLICITUD_DESCARGA_NO_TERMINADA",
                $"No se puede marcar Cosechada desde estado {Estado}.");
        Estado = EstadoSolicitudDescarga.Cosechada;
        CosechadaAt = ahora;
    }

    /// <summary>Cierra la solicitud (consumidor ack-ó todos los UUIDs).</summary>
    public void Cerrar(DateTimeOffset ahora)
    {
        if (Estado != EstadoSolicitudDescarga.Cosechada)
            throw new BusinessRuleException("SOLICITUD_DESCARGA_NO_COSECHADA",
                $"No se puede cerrar desde estado {Estado}.");
        Estado = EstadoSolicitudDescarga.Cerrada;
        CerradaAt = ahora;
    }

    /// <summary>Marca como Error con código y mensaje (uso del worker).</summary>
    public void MarcarError(string codigo, string mensaje, DateTimeOffset ahora)
    {
        Estado = EstadoSolicitudDescarga.Error;
        ErrorCodigo = codigo;
        ErrorMensaje = mensaje;
        LastPollAt = ahora;
    }
}

/// <summary>
/// FSM interna de <see cref="SolicitudDescarga"/>. Numerada para
/// persistir como smallint y para mantener orden cronológico en
/// queries ORDER BY estado.
/// </summary>
public enum EstadoSolicitudDescarga
{
    /// <summary>Persistida localmente pero no confirmada en FiscalAPI todavía.</summary>
    Pendiente = 1,
    /// <summary>FiscalAPI tiene la solicitud; el SAT aún no la marca Terminada.</summary>
    EsperandoSat = 2,
    /// <summary>SAT terminó; FiscalAPI está procesando los paquetes.</summary>
    EsperandoApi = 3,
    /// <summary>FiscalAPI completó. Listo para cosecha de /meta-items.</summary>
    Terminada = 4,
    /// <summary>Cosechado: meta-items entregados a <c>ICfdiIngestaPort</c>.</summary>
    Cosechada = 5,
    /// <summary>Consumidor (CxP) acusó recibo. Mantenida para auditoría.</summary>
    Cerrada = 6,
    /// <summary>Estado terminal de error.</summary>
    Error = -1,
}
