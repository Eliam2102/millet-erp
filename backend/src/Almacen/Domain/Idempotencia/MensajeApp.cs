namespace Millet.Almacen.Domain.Idempotencia;

/// <summary>
/// Mensaje recibido de la app de Almacenes (contrato v1, sobre con mensajeId
/// idempotente). Tabla <c>almacen.mensajes_app</c>; PK = mensaje_id, así la
/// carrera de dos inserciones concurrentes la resuelve la BD.
/// </summary>
public sealed class MensajeApp
{
    public string MensajeId { get; private set; } = string.Empty;
    public string Tipo { get; private set; } = string.Empty;
    public string? Referencia { get; private set; }
    public string Payload { get; private set; } = "{}";
    public string Estado { get; private set; } = string.Empty;
    public string? Folio { get; private set; }
    public string? Error { get; private set; }
    public Guid? DocumentoId { get; private set; }
    public DateTimeOffset RecibidoAt { get; private set; }

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
}
