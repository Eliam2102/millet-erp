namespace Millet.SharedKernel.Domain.Adjuntos;

/// <summary>
/// Estado derivado de un <see cref="Adjunto"/> a una fecha dada (no se
/// persiste: depende de "hoy"). F1-ADM-11 G1.2.
/// </summary>
public enum EstadoAdjunto
{
    /// <summary>Con vigencia y aún lejos de vencer.</summary>
    Vigente = 1,

    /// <summary>Vence en <see cref="Adjunto.DiasAvisoVencimiento"/> días o menos.</summary>
    PorVencer = 2,

    /// <summary>La fecha de vigencia ya pasó.</summary>
    Vencido = 3,

    /// <summary>El tipo de documento no maneja vigencia.</summary>
    SinVigencia = 4,

    /// <summary>Dado de baja lógica (irreversible).</summary>
    Baja = 5,
}
