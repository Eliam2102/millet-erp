using Millet.Integraciones.Aw.Domain.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Integraciones.Aw.Domain;

/// <summary>
/// Agregado raíz de una "cosa sincronizada con A+W": cotización, pedido,
/// cliente, artículo o inventario. Una sola tabla con discriminador
/// <see cref="TipoEntidad"/>. Ciclo de vida en
/// <c>docs/integration/02-edi-correlation.md</c> §3.5.
///
/// <para>
/// <b>Multi-empresa (ADR-0011):</b> implementa <see cref="IPerteneceAEmpresa"/>;
/// el <c>EmpresaContextSaveChangesInterceptor</c> setea
/// <see cref="EmpresaId"/> en INSERT y el query filter global filtra por
/// la empresa del JWT actual.
/// </para>
///
/// <para>
/// <b>idempotency_key:</b> el doc 02 §3.1 propone almacenarla aquí; en
/// PR B (D-IDEMPOTENCY) se omite — el <c>IdempotencyMiddleware</c> en
/// <c>core.idempotency_keys</c> es la única fuente de verdad de
/// idempotency HTTP. La unicidad de negocio (cliente NO debe enviar dos
/// cotizaciones con el mismo <c>quote_reference</c>) la garantiza el
/// UNIQUE constraint <c>uq_tipo_referencia</c>. Si el doc llega a
/// revisarse, alinear allá.
/// </para>
/// </summary>
public sealed class EntidadExterna : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public TipoEntidad TipoEntidad { get; private set; }
    public string ReferenciaExterna { get; private set; } = string.Empty;
    public Guid EmpresaId { get; set; }

    /// <summary>
    /// Código corto de la sucursal origen (3 chars, uppercase). Ya NO se
    /// embebe en el filename del EDI (el depto viaja dentro del EDI y lo
    /// sobrescribe A+W con un único customizing; ver runbook aw-customizing
    /// v4). Se conserva para el push realtime al Glass Agent y trazabilidad.
    /// Valores válidos viven en <c>IntegracionesAw:Sucursales</c> del
    /// appsettings; el validator del command rechaza valores fuera de catálogo.
    /// </summary>
    public string Sucursal { get; private set; } = string.Empty;

    public string PayloadOriginal { get; private set; } = "{}";
    public Guid? PayloadBlobId { get; private set; }
    public string? EdiContent { get; private set; }
    public EstadoEntidad Estado { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset? DeliveredToAwAt { get; private set; }
    public DateTimeOffset? CorrelatedAt { get; private set; }
    public long? AwDocId { get; private set; }
    public string? AwDocIdSecondary { get; private set; }
    public Guid? SubmittedBySpnId { get; private set; }
    public string? LastError { get; private set; }
    public short RetryCount { get; private set; }
    public string? ResolutionNote { get; private set; }

    // ───────── PR #198 (callback model) ─────────
    // Datos del outcome de A+W reportados por el drop service "per-EDI".
    // Distintos de LastError, que es el error de NUESTRO sistema (drop
    // HTTP, conexión, etc.). Estos vienen del parseo del log de A+W
    // (g_lsUdv[0]).

    /// <summary>
    /// Códigos de error de A+W al rechazar el EDI (ej. <c>1550,1603,1555</c>).
    /// Almacenado como CSV simple. NULL si no hubo rechazo de A+W.
    /// Poblado por <see cref="MarcarCorrelacionFallidaDirectamente"/>.
    /// </summary>
    public string? AwErrorCodes { get; private set; }

    /// <summary>
    /// Mensaje user-friendly construido a partir de los códigos de A+W
    /// (ej. "Artículo M7916 no existe + posiciones incompletas"). NULL si
    /// no hubo rechazo. Lo construye el drop service desde el log.
    /// </summary>
    public string? AwErrorMessage { get; private set; }

    /// <summary>
    /// Slice del log <c>g_lsUdv[0]</c> que A+W generó para este EDI.
    /// Truncado por el drop service a ~2KB. Útil para soporte sin tener
    /// que entrar a SER-DATA. Opcional para casos success y failed.
    /// </summary>
    public string? AwDiagnosticLog { get; private set; }

    // ───────── PDF de A+W (oferta/pedido) ─────────
    // El AwDocumentSyncWorker descarga el PDF que A+W exporta
    // (oferta_<aw_doc_id>.pdf / pedido_<aw_doc_id>.pdf) del drop service,
    // lo sube a Blob Storage y referencia aquí la URL interna. El Glass
    // Agent NUNCA recibe esta URL — accede vía el endpoint proxy
    // autenticado GET /cotizaciones/{id}/pdf.

    /// <summary>
    /// URL interna de Blob Storage del PDF descargado de A+W (HTTPS en
    /// prod, <c>file://</c> en dev). NULL hasta que el worker lo adjunte.
    /// No se expone al cliente.
    /// </summary>
    public string? PdfBlobUrl { get; private set; }

    /// <summary>
    /// Nombre original del PDF en A+W (ej. <c>oferta_4614.pdf</c>). Se usa
    /// como <c>fileDownloadName</c> al servir el endpoint proxy.
    /// </summary>
    public string? PdfFilename { get; private set; }

    /// <summary>
    /// Momento REAL en que A+W exportó el PDF (= <c>modified_at</c> del
    /// archivo en la carpeta de A+W), NO el de correlación ni el de subida
    /// a blob. Permite medir el tiempo correlación→entrega del PDF.
    /// </summary>
    public DateTimeOffset? PdfUploadedAt { get; private set; }

    private EntidadExterna() { } // EF Core

    /// <summary>
    /// Constructor de creación. Estado inicial = <see cref="EstadoEntidad.Submitted"/>.
    /// El handler <c>RegistrarCotizacionEdi</c> lo invoca tras validar el
    /// payload.
    /// </summary>
    public EntidadExterna(
        Guid id,
        TipoEntidad tipoEntidad,
        string referenciaExterna,
        Guid empresaId,
        string payloadOriginal,
        string? ediContent,
        Guid? submittedBySpnId,
        DateTimeOffset submittedAt,
        string sucursal) : base(id)
    {
        if (string.IsNullOrWhiteSpace(referenciaExterna))
            throw new ArgumentException("ReferenciaExterna es requerida.", nameof(referenciaExterna));
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId es requerida.", nameof(empresaId));
        if (string.IsNullOrWhiteSpace(payloadOriginal))
            throw new ArgumentException("PayloadOriginal es requerido (JSON serializado).", nameof(payloadOriginal));
        if (string.IsNullOrWhiteSpace(sucursal))
            throw new ArgumentException("Sucursal es requerida.", nameof(sucursal));
        if (sucursal.Length > 10)
            throw new ArgumentException("Sucursal excede 10 caracteres.", nameof(sucursal));

        TipoEntidad = tipoEntidad;
        ReferenciaExterna = referenciaExterna;
        EmpresaId = empresaId;
        Sucursal = sucursal;
        PayloadOriginal = payloadOriginal;
        EdiContent = ediContent;
        SubmittedBySpnId = submittedBySpnId;
        Estado = EstadoEntidad.Submitted;
        SubmittedAt = submittedAt;
        RetryCount = 0;
    }

    /// <summary>
    /// Incrementa el contador de reintentos y persiste el último error.
    /// NO cambia el estado — el drop sigue siendo reintentable. Llamado
    /// por <c>AwDropWorker</c> en fallos transitorios (IsTerminal=false).
    /// </summary>
    public void IncrementarRetry(string error, string errorKind)
    {
        if (Estado is EstadoEntidad.Correlated or EstadoEntidad.ManuallyResolved)
        {
            throw new InvalidStateTransitionException(Estado, nameof(IncrementarRetry));
        }
        RetryCount++;
        LastError = $"[{errorKind}] {error}";
    }

    /// <summary>
    /// Transición a <see cref="EstadoEntidad.FailedDrop"/>: el drop falló
    /// tras agotar reintentos. Llamado por <c>AwDropWorker</c> con
    /// IsTerminal=true. Estado activo — el operador puede reintentar
    /// manualmente.
    /// </summary>
    public void MarcarDropFalladoTerminal(string error, string errorKind)
    {
        if (Estado is not EstadoEntidad.Submitted and not EstadoEntidad.FailedDrop)
        {
            throw new InvalidStateTransitionException(Estado, nameof(MarcarDropFalladoTerminal));
        }
        Estado = EstadoEntidad.FailedDrop;
        LastError = $"[{errorKind}] {error}";
    }

    /// <summary>
    /// Reactiva una cotización fallada para reintentar el flujo completo
    /// (drop → correlación). Resetea contador de reintentos y limpia el
    /// último error registrado. Llamado por el endpoint
    /// <c>POST /cotizaciones/{id}/reintentar</c> (PR D).
    ///
    /// <para>
    /// Solo permitido desde estados terminales no-deseados:
    /// <see cref="EstadoEntidad.FailedDrop"/> (drop al on-prem agotó
    /// reintentos) o <see cref="EstadoEntidad.ManuallyResolved"/>
    /// (operador marcó resuelto y luego cambió de opinión). Throw si
    /// el estado actual es activo (<c>Submitted</c>, <c>AwaitingCorrelation</c>)
    /// — debe esperar al desenlace natural — o terminal exitoso
    /// (<c>Correlated</c>) — no tiene sentido reintentar.
    /// </para>
    ///
    /// <para>
    /// <c>SubmittedAt</c> NO se actualiza — es el momento original.
    /// // PLATFORM-TODO(&lt;ResubmittedAtTracking&gt;): si surge necesidad
    /// de auditar reintentos, agregar campo <c>ResubmittedAt</c> y
    /// columna correspondiente.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidStateTransitionException">
    /// Si el estado actual no permite reintento.
    /// </exception>
    public void Reintentar()
    {
        if (Estado is not EstadoEntidad.FailedDrop
            and not EstadoEntidad.ManuallyResolved)
        {
            throw new InvalidStateTransitionException(Estado, nameof(Reintentar));
        }

        Estado = EstadoEntidad.Submitted;
        RetryCount = 0;
        LastError = null;
        AwErrorCodes = null;
        AwErrorMessage = null;
        AwDiagnosticLog = null;
    }

    // ───────── PR #198 (callback model — usados por PR #201) ─────────

    /// <summary>
    /// Transición <c>Submitted/AwaitingCorrelation → Correlated</c> en el
    /// modelo "callback per-EDI" (PR #198). El <c>AwDropWorker</c> la
    /// invoca cuando el drop service reporta <see cref="Application.Ports.DropOutcome.Success"/>
    /// — el log de A+W trae el <paramref name="awDocId"/> en el código
    /// <c>(4614)</c> y no hace falta consultar SQL.
    ///
    /// <para>
    /// Idempotente si ya está Correlated con el mismo <paramref name="awDocId"/>.
    /// Throw si está Correlated con un <c>AwDocId</c> distinto (data quality
    /// issue: alguien correlacionó a otro doc).
    /// </para>
    ///
    /// <para>
    /// <b>Late reconciliation (PR-4 del feature):</b> con
    /// <paramref name="permitirDesdeFailedDrop"/> = true, también acepta la
    /// transición desde <c>FailedDrop</c> SI el <c>LastError</c> originó en
    /// un timeout (kind <c>aw_processing_timeout</c>) — caso en que A+W
    /// procesó el EDI después del timeout sync del drop service. Solo el
    /// <c>AwLateReconciliationWorker</c> debe pasar el flag; el flow normal
    /// (Submitted) lo deja en su default false. La fricción evita
    /// reconciliaciones inesperadas desde otros estados terminales.
    /// </para>
    /// </summary>
    public void MarcarCorrelacionadaDirectamente(
        long? awDocId,
        DateTimeOffset correlatedAt,
        string? diagnosticLog = null,
        bool permitirDesdeFailedDrop = false)
    {
        if (Estado is EstadoEntidad.Correlated && AwDocId == awDocId)
        {
            // Idempotente (incluye caso awDocId == null en ambos lados).
            return;
        }
        if (Estado is EstadoEntidad.Correlated && AwDocId != awDocId)
        {
            throw new InvalidStateTransitionException(Estado,
                $"MarcarCorrelacionadaDirectamente(awDocId={awDocId}, persisted AwDocId={AwDocId})");
        }
        var lateOk =
            permitirDesdeFailedDrop
            && Estado is EstadoEntidad.FailedDrop
            && LastErrorKind() == "aw_processing_timeout";
        if (Estado is not EstadoEntidad.Submitted && !lateOk)
        {
            throw new InvalidStateTransitionException(Estado, nameof(MarcarCorrelacionadaDirectamente));
        }

        Estado = EstadoEntidad.Correlated;
        AwDocId = awDocId;
        CorrelatedAt = correlatedAt;
        // Si venía directo de Submitted (drop sync con outcome), no había
        // pasado por AwaitingCorrelation — registramos la "entrega" en el
        // mismo instante para mantener el campo consistente.
        DeliveredToAwAt ??= correlatedAt;
        LastError = null;
        AwErrorCodes = null;
        AwErrorMessage = null;
        AwDiagnosticLog = diagnosticLog;
    }

    /// <summary>
    /// Transición <c>Submitted/AwaitingCorrelation → FailedCorrelation</c>
    /// cuando A+W RECHAZÓ el EDI (códigos terminales tipo <c>(1555)</c>).
    /// Distinto de <see cref="MarcarCorrelacionExpirada"/> (timeout sin
    /// match): aquí A+W procesó y dijo NO. Llamado por el worker tras
    /// recibir <see cref="Application.Ports.DropOutcome.Failed"/> del
    /// drop service.
    ///
    /// <para>
    /// Idempotente si ya está FailedCorrelation: refresca los códigos y
    /// mensaje con la información más reciente.
    /// </para>
    ///
    /// <para>
    /// <b>Late reconciliation (PR-4 del feature):</b> con
    /// <paramref name="permitirDesdeFailedDrop"/> = true, también acepta la
    /// transición desde <c>FailedDrop</c> SI el <c>LastError</c> originó en
    /// un timeout — caso en que A+W procesó tarde y el log resultante
    /// reportó <c>outcome=failed</c> con códigos terminales.
    /// </para>
    /// </summary>
    public void MarcarCorrelacionFallidaDirectamente(
        IReadOnlyList<string> errorCodes,
        string errorMessage,
        DateTimeOffset failedAt,
        string? diagnosticLog = null,
        bool permitirDesdeFailedDrop = false)
    {
        if (errorCodes is null || errorCodes.Count == 0)
            throw new ArgumentException("Debe haber al menos un código de error.", nameof(errorCodes));
        if (string.IsNullOrWhiteSpace(errorMessage))
            throw new ArgumentException("Mensaje de error requerido.", nameof(errorMessage));

        var codesCsv = string.Join(",", errorCodes);

        if (Estado is EstadoEntidad.FailedCorrelation)
        {
            // Idempotente — refresca contexto si se reportó de nuevo.
            AwErrorCodes = codesCsv;
            AwErrorMessage = errorMessage;
            AwDiagnosticLog = diagnosticLog;
            LastError = $"[aw_rejected] {errorMessage}";
            return;
        }
        var lateOk =
            permitirDesdeFailedDrop
            && Estado is EstadoEntidad.FailedDrop
            && LastErrorKind() == "aw_processing_timeout";
        if (Estado is not EstadoEntidad.Submitted && !lateOk)
        {
            throw new InvalidStateTransitionException(Estado, nameof(MarcarCorrelacionFallidaDirectamente));
        }

        Estado = EstadoEntidad.FailedCorrelation;
        DeliveredToAwAt ??= failedAt;
        AwErrorCodes = codesCsv;
        AwErrorMessage = errorMessage;
        AwDiagnosticLog = diagnosticLog;
        LastError = $"[aw_rejected] {errorMessage}";
    }

    /// <summary>
    /// Extrae el <c>kind</c> del <see cref="LastError"/> que sigue la
    /// convención <c>[kind] message</c>. Devuelve <c>null</c> si no hay
    /// error o el formato no matchea.
    /// </summary>
    private string? LastErrorKind()
    {
        if (string.IsNullOrEmpty(LastError)) return null;
        if (LastError.Length < 3 || LastError[0] != '[') return null;
        var closeIdx = LastError.IndexOf(']');
        if (closeIdx <= 1) return null;
        return LastError.Substring(1, closeIdx - 1);
    }

    /// <summary>
    /// Transición <c>Submitted → FailedDrop</c> con kind
    /// <c>aw_processing_timeout</c>: el drop service escribió el archivo
    /// en <c>Work\</c> pero A+W no lo procesó dentro del timeout. Es
    /// reintentable (el archivo puede haber quedado para el siguiente
    /// ciclo del scheduler A+W).
    /// </summary>
    public void MarcarStuck(int waitedMs, string? lane = null)
    {
        var msg = $"A+W no procesó en {waitedMs}ms";
        if (!string.IsNullOrWhiteSpace(lane)) msg += $" (lane={lane})";
        MarcarDropFalladoTerminal(msg, "aw_processing_timeout");
    }

    /// <summary>
    /// Resolución manual desde un endpoint admin (PR D futuro). Solo
    /// permitido desde estados activos no-finales. Marca el estado como
    /// terminal con nota.
    /// </summary>
    public void MarcarResueltoManual(string nota, Guid operadorId)
    {
        if (Estado is EstadoEntidad.Correlated or EstadoEntidad.ManuallyResolved)
        {
            throw new InvalidStateTransitionException(Estado, nameof(MarcarResueltoManual));
        }
        if (string.IsNullOrWhiteSpace(nota))
            throw new ArgumentException("Nota es requerida para resolución manual.", nameof(nota));

        Estado = EstadoEntidad.ManuallyResolved;
        ResolutionNote = $"[operador={operadorId}] {nota}";
    }

    /// <summary>
    /// Adjunta el PDF que A+W exportó (oferta/pedido) tras subirlo a Blob
    /// Storage. Lo invoca el <c>AwDocumentSyncWorker</c> cuando descarga el
    /// archivo del drop service. NO cambia el estado del agregado — el PDF
    /// es un artefacto adjunto, ortogonal al ciclo de vida de correlación.
    ///
    /// <para>
    /// Idempotente: re-adjuntar el mismo <paramref name="blobUrl"/> es no-op.
    /// Si llega un blob distinto (A+W re-exportó el PDF), se actualiza la
    /// referencia — el worker dedupea por <see cref="PdfBlobUrl"/> nula, así
    /// que en práctica solo se adjunta una vez.
    /// </para>
    /// </summary>
    public void AdjuntarDocumentoPdf(string blobUrl, string filename, DateTimeOffset uploadedAt)
    {
        if (string.IsNullOrWhiteSpace(blobUrl))
            throw new ArgumentException("blobUrl es requerido.", nameof(blobUrl));
        if (string.IsNullOrWhiteSpace(filename))
            throw new ArgumentException("filename es requerido.", nameof(filename));

        if (string.Equals(PdfBlobUrl, blobUrl, StringComparison.Ordinal))
        {
            return; // Idempotente.
        }

        PdfBlobUrl = blobUrl;
        PdfFilename = filename;
        PdfUploadedAt = uploadedAt;
    }
}
