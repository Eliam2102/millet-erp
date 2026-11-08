namespace Millet.SharedKernel.Domain.Integraciones;

/// <summary>Por qué se abrió una alerta del tablero de integraciones (I13.6).</summary>
public enum TipoAlertaIntegracion : short
{
    /// <summary>Hay mensajes que agotaron sus reintentos.</summary>
    MensajesEnError = 1,
    /// <summary>Hay mensajes pendientes o en reintento más viejos que el umbral: el flujo no avanza.</summary>
    FlujoDetenido = 2,
}

/// <summary>
/// Alerta de una integración (I13.6, R9 del Módulo 13): se abre cuando una fuente
/// tiene mensajes en error o se detiene, se avisa por correo una vez y se cierra
/// sola cuando la condición desaparece (recuperación, también avisada). Mientras
/// está abierta no se vuelve a avisar, aunque el detalle cambie.
/// </summary>
public sealed class AlertaIntegracion : BaseEntity, INotAudited
{
    public string FuenteClave { get; private set; } = string.Empty;
    public string FuenteNombre { get; private set; } = string.Empty;
    public TipoAlertaIntegracion Tipo { get; private set; }
    public string Detalle { get; private set; } = string.Empty;
    public DateTimeOffset AbiertaEn { get; private set; }
    public DateTimeOffset? NotificadaEn { get; private set; }
    public DateTimeOffset? CerradaEn { get; private set; }
    public DateTimeOffset? RecuperacionNotificadaEn { get; private set; }

    public bool Abierta => CerradaEn is null;

    private AlertaIntegracion() { }

    public static AlertaIntegracion Abrir(string fuenteClave, string fuenteNombre, TipoAlertaIntegracion tipo, string detalle, DateTimeOffset ahora) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            FuenteClave = fuenteClave,
            FuenteNombre = Recortar(fuenteNombre, 200),
            Tipo = tipo,
            Detalle = Recortar(detalle, MaximoDetalle),
            AbiertaEn = ahora,
        };

    /// <summary>El detalle refleja la última evaluación (p. ej. cuántos mensajes hay en error).</summary>
    public void Actualizar(string detalle)
    {
        if (Abierta) Detalle = Recortar(detalle, MaximoDetalle);
    }

    public void MarcarNotificada(DateTimeOffset ahora) => NotificadaEn ??= ahora;

    public void Cerrar(DateTimeOffset ahora)
    {
        if (Abierta) CerradaEn = ahora;
    }

    public void MarcarRecuperacionNotificada(DateTimeOffset ahora) => RecuperacionNotificadaEn ??= ahora;

    public const int MaximoDetalle = 1000;

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];
}
