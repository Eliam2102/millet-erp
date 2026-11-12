namespace Millet.Integraciones.Aw.Application.TipoCambio;

/// <summary>
/// Opciones de configuración para la publicación y supervisión de tipo de cambio hacia A+W (I13.2).
/// </summary>
public sealed class AwTipoCambioOptions
{
    public const string Seccion = "IntegracionesAw:TipoCambio";
    public const string SectionName = Seccion;

    /// <summary>
    /// Habilita o deshabilita la publicación periódica y supervisión hacia A+W.
    /// Por defecto false hasta obtener visto bueno de TI y validar conectividad a la tabla puente.
    /// </summary>
    public bool Habilitado { get; set; }

    /// <summary>
    /// Hora límite en formato HH:mm (hora centro México) para alertar si el TC de mañana no ha sido publicado.
    /// </summary>
    public string HoraLimitePublicacion { get; set; } = "18:00";

    /// <summary>
    /// Hora límite en formato HH:mm (hora centro México) para alertar (log y correo) si A+W no ha aplicado el TC de hoy. 08:00 acordado con TI de Millet el 10-nov.
    /// </summary>
    public string HoraLimiteAplicacion { get; set; } = "08:00";

    /// <summary>
    /// Lista de monedas ISO 4217 a publicar hacia A+W (por defecto USD, EUR y CAD).
    /// </summary>
    public List<string> Monedas { get; set; } = ["USD", "EUR", "CAD"];
}
