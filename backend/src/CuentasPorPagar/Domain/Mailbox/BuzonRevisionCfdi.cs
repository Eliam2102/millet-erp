using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.Mailbox;

/// <summary>
/// Bandeja de revisión del buzón de CFDIs (P8.1-a): un correo que no se pudo
/// convertir en CFDI recibido (sin XML, XML inválido, receptor que no es
/// Millet, adjunto demasiado grande...). Una fila por correo
/// (<see cref="MensajeId"/> único). Guarda solo metadatos: nunca el cuerpo
/// del correo ni el XML.
/// <para>
/// <see cref="EmpresaId"/> es nulo cuando el motivo impide saber la empresa
/// (p. ej. RFC receptor desconocido).
/// </para>
/// </summary>
public sealed class BuzonRevisionCfdi : BaseEntity, IAuditable
{
    public const int MaxMensajeId = 400;

    public Guid? EmpresaId { get; private set; }
    public string MensajeId { get; private set; } = default!;
    public string Asunto { get; private set; } = default!;
    public string Remitente { get; private set; } = default!;
    public DateTimeOffset RecibidoEn { get; private set; }
    public string Motivo { get; private set; } = default!;
    public DateTimeOffset RegistradoEn { get; private set; }
    public bool Descartado { get; private set; }
    public string? MotivoDescarte { get; private set; }

    private BuzonRevisionCfdi() { }

    public static BuzonRevisionCfdi Registrar(
        Guid? empresaId, MailboxMensaje mensaje, string motivo, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        EmpresaId = empresaId,
        MensajeId = mensaje.Id.Length > MaxMensajeId ? mensaje.Id[..MaxMensajeId] : mensaje.Id,
        Asunto = mensaje.AsuntoTruncado,
        Remitente = mensaje.FromAddress.Length > 320 ? mensaje.FromAddress[..320] : mensaje.FromAddress,
        RecibidoEn = mensaje.ReceivedAt,
        Motivo = motivo.Length > 60 ? motivo[..60] : motivo,
        RegistradoEn = ahora,
    };

    public void Descartar(string motivo)
    {
        if (Descartado)
            throw new Millet.SharedKernel.Application.Exceptions.BusinessRuleException(
                "BUZON_REVISION_YA_DESCARTADA", "El correo ya fue descartado.");
        Descartado = true;
        MotivoDescarte = motivo.Length > 400 ? motivo[..400] : motivo;
    }
}

/// <summary>Bitácora persistida de cada corrida del worker del buzón (P8.1).</summary>
public sealed class BuzonEjecucion : BaseEntity, INotAudited
{
    public DateTimeOffset InicioUtc { get; private set; }
    public DateTimeOffset FinUtc { get; private set; }
    public int Mensajes { get; private set; }
    public int Ingresados { get; private set; }
    public int Duplicados { get; private set; }
    public int ARevision { get; private set; }

    /// <summary>Código del error que abortó la corrida (p. ej. MAILBOX_LIST_FALLO); nulo si terminó.</summary>
    public string? Error { get; private set; }

    private BuzonEjecucion() { }

    public static BuzonEjecucion Registrar(
        DateTimeOffset inicio, DateTimeOffset fin, int mensajes, int ingresados, int duplicados, int aRevision, string? error) => new()
    {
        Id = Guid.CreateVersion7(),
        InicioUtc = inicio, FinUtc = fin, Mensajes = mensajes, Ingresados = ingresados,
        Duplicados = duplicados, ARevision = aRevision, Error = error,
    };
}
