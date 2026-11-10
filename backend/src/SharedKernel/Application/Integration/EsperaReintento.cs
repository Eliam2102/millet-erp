namespace Millet.SharedKernel.Application.Integration;

/// <summary>
/// Espera creciente (<i>backoff</i> exponencial con tope) entre reintentos de una
/// integración (I13.6, R9 del Módulo 13): tras la falla <c>n</c> se espera
/// <c>base × 2^(n−1)</c>, sin pasar del tope. Así un canal caído no se martilla y
/// los mensajes aguantan una caída larga antes de agotar sus intentos.
/// </summary>
public static class EsperaReintento
{
    /// <summary>Espera antes del siguiente intento, dado cuántas fallas lleva el mensaje.</summary>
    /// <param name="fallas">Intentos fallidos hasta ahora; con 0 no hay espera.</param>
    /// <param name="baseEspera">Espera tras la primera falla.</param>
    /// <param name="tope">Espera máxima entre dos intentos.</param>
    public static TimeSpan Calcular(int fallas, TimeSpan baseEspera, TimeSpan tope)
    {
        if (fallas <= 0 || baseEspera <= TimeSpan.Zero) return TimeSpan.Zero;
        if (tope < baseEspera) tope = baseEspera;
        // 2^30 ya rebasa cualquier tope razonable; evita desbordar el multiplicador.
        var factor = Math.Pow(2, Math.Min(fallas - 1, 30));
        var segundos = Math.Min(baseEspera.TotalSeconds * factor, tope.TotalSeconds);
        return TimeSpan.FromSeconds(segundos);
    }

    /// <summary>Tiempo total que un mensaje sigue reintentándose antes de agotar <paramref name="maxIntentos"/>.</summary>
    public static TimeSpan Total(int maxIntentos, TimeSpan baseEspera, TimeSpan tope)
    {
        var total = TimeSpan.Zero;
        for (var n = 1; n < maxIntentos; n++) total += Calcular(n, baseEspera, tope);
        return total;
    }
}
