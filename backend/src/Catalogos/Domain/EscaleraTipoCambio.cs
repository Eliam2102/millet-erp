namespace Millet.Catalogos.Domain;

/// <summary>Valor que rige una fecha de operación y el FIX del que sale.</summary>
public sealed record AplicacionTipoCambio(DateOnly Fecha, DateOnly FechaFix, decimal Valor);

/// <summary>
/// Regla de la «escalera» del tipo de cambio (K10.6, regla de Millet del 25-oct):
/// Banxico determina el FIX el día hábil bancario D, lo publica en el DOF el día hábil
/// siguiente y una operación en la fecha X usa el valor publicado en el DOF el día hábil
/// bancario inmediato anterior a X. Sábados, domingos y festivos repiten el último valor.
/// EUR y CAD siguen la misma regla con su propio valor del día D.
///
/// <para>
/// El calendario bancario sale de las fechas con FIX publicado: no se usa el calendario
/// de festivos laborales (<c>system.dias-festivos</c>) porque los inhábiles bancarios son
/// otros (Jueves y Viernes Santo, 12-dic).
/// </para>
/// </summary>
public static class EscaleraTipoCambio
{
    public const int Decimales = 4;

    /// <summary>
    /// Último día cuyo carácter hábil o inhábil ya se conoce. Hoy cuenta si ya tiene FIX;
    /// si no, hasta ayer (el FIX de un día hábil sale a las 12:00 de ese día). Los sábados y
    /// domingos que siguen nunca son hábiles, así que se conocen por adelantado.
    /// </summary>
    public static DateOnly CalendarioConocidoHasta(IReadOnlySet<DateOnly> diasHabiles, DateOnly hoy)
    {
        var conocido = diasHabiles.Contains(hoy) ? hoy : hoy.AddDays(-1);
        while (EsFinDeSemana(conocido.AddDays(1))) conocido = conocido.AddDays(1);
        return conocido;
    }

    /// <summary>
    /// Valores para las fechas de operación de <paramref name="desde"/> a <paramref name="hasta"/>.
    /// Una fecha X solo se calcula si el calendario se conoce hasta X-1; se omite si falta el
    /// FIX que le corresponde (nunca se inventa un TC).
    /// </summary>
    public static IReadOnlyList<AplicacionTipoCambio> Aplicar(
        IReadOnlyDictionary<DateOnly, decimal> fixPorFecha,
        IReadOnlySet<DateOnly> diasHabiles,
        DateOnly calendarioConocidoHasta,
        DateOnly desde,
        DateOnly hasta)
    {
        var habiles = diasHabiles.Where(d => d <= calendarioConocidoHasta).Order().ToArray();
        var resultado = new List<AplicacionTipoCambio>();
        for (var fecha = desde; fecha <= hasta && fecha.AddDays(-1) <= calendarioConocidoHasta; fecha = fecha.AddDays(1))
        {
            if (HabilAnterior(habiles, fecha) is not { } publicacionDof) continue;
            if (HabilAnterior(habiles, publicacionDof) is not { } fechaFix) continue;
            if (!fixPorFecha.TryGetValue(fechaFix, out var valor)) continue;
            resultado.Add(new AplicacionTipoCambio(fecha, fechaFix,
                Math.Round(valor, Decimales, MidpointRounding.AwayFromZero)));
        }
        return resultado;
    }

    private static DateOnly? HabilAnterior(DateOnly[] habiles, DateOnly fecha)
    {
        var i = Array.BinarySearch(habiles, fecha);
        var anterior = (i >= 0 ? i : ~i) - 1;
        return anterior >= 0 ? habiles[anterior] : null;
    }

    private static bool EsFinDeSemana(DateOnly fecha) => fecha.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}
