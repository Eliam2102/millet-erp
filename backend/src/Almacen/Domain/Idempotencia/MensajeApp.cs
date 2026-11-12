namespace Millet.Almacen.Domain.Idempotencia;

/// <summary>
/// Mensaje recibido de la app de Almacenes (contrato v1, sobre con mensajeId
/// idempotente). Tabla <c>almacen.mensajes_app</c>; PK = mensaje_id, así la
/// carrera de dos inserciones concurrentes la resuelve la BD.
/// </summary>
public sealed class MensajeApp
{
    /// <summary>M-223: ROTURA de la app recibida que aún no baja el saldo; espera la autorización de un usuario del ERP.</summary>
    public const string Pendiente = "PENDIENTE";
    /// <summary>M-223: ROTURA rechazada en el ERP; no movió nada.</summary>
    public const string Rechazado = "RECHAZADO";

    public string MensajeId { get; private set; } = string.Empty;
    public string Tipo { get; private set; } = string.Empty;
    public string? Referencia { get; private set; }
    public string Payload { get; private set; } = "{}";
    public string Estado { get; private set; } = string.Empty;
    public string? Folio { get; private set; }
    public string? Error { get; private set; }
    public Guid? DocumentoId { get; private set; }
    public DateTimeOffset RecibidoAt { get; private set; }
    public Guid? DecididoPor { get; private set; }
    public DateTimeOffset? DecididoAt { get; private set; }
    public string? MotivoRechazo { get; private set; }

    private MensajeApp() { }

    public MensajeApp(string mensajeId, string tipo, string? referencia, string payload,
        string estado, string? folio, string? error)
    {
        MensajeId = mensajeId;
        Tipo = tipo;
        Referencia = referencia;
        Payload = payload;
        Estado = estado;
        Folio = folio;
        Error = error;
        RecibidoAt = DateTimeOffset.UtcNow;
    }

    /// <summary>M-223: la rotura pendiente ya bajó el saldo con los movimientos <paramref name="folio"/>.</summary>
    public void Autorizar(Guid usuarioId, string? folio)
    {
        if (Estado != Pendiente) throw new InvalidOperationException($"La rotura {MensajeId} ya fue decidida ({Estado}).");
        Estado = "RECIBIDO"; Folio = folio; Error = null; DecididoPor = usuarioId; DecididoAt = DateTimeOffset.UtcNow;
    }

    /// <summary>M-223: rechazada en el ERP; el saldo no cambia.</summary>
    public void Rechazar(Guid usuarioId, string motivo)
    {
        if (Estado != Pendiente) throw new InvalidOperationException($"La rotura {MensajeId} ya fue decidida ({Estado}).");
        Estado = Rechazado; MotivoRechazo = motivo; DecididoPor = usuarioId; DecididoAt = DateTimeOffset.UtcNow;
    }
}
