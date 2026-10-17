using Millet.CuentasPorPagar.Domain.Notificaciones;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Calendario;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.Revision;

/// <summary>
/// Regla del plazo (SLA) de una factura detenida por un motivo de revisión.
/// El plazo se mide en días hábiles: 5 días hábiles por defecto, 15 para
/// Disputa contractual y sin plazo para Indicación expresa. El día de entrada
/// a revisión (fecha local de México) no cuenta; el día de evaluación cuenta
/// solo si es hábil.
///
/// <para>
/// Umbrales de aviso por plazo: aviso al gerente, plazo vencido y escalamiento
/// a Dirección de Finanzas (3/5/10 para plazo 5; 8/12/20 para plazo 15).
/// </para>
/// </summary>
public static class PlazoRevision
{
    /// <summary>Días hábiles a partir de los cuales se emite cada nivel de aviso.</summary>
    public static (int Aviso, int Vencido, int Escalamiento) Umbrales(int slaDias) => slaDias switch
    {
        5 => (3, 5, 10),
        15 => (8, 12, 20),
        _ => ((int)Math.Ceiling(slaDias * 0.6m), slaDias, slaDias * 2),
    };

    /// <summary>Nivel de aviso que corresponde a los días hábiles transcurridos; null si aún no toca avisar o el motivo no tiene plazo.</summary>
    public static NivelEscalamientoSla? Nivel(int diasHabiles, int? slaDias)
    {
        if (slaDias is null || diasHabiles <= 0) return null;
        var (aviso, vencido, escalamiento) = Umbrales(slaDias.Value);
        if (diasHabiles >= escalamiento) return NivelEscalamientoSla.EscalamientoDirector;
        if (diasHabiles >= vencido) return NivelEscalamientoSla.SlaVencido;
        if (diasHabiles >= aviso) return NivelEscalamientoSla.AvisoGerente;
        return null;
    }

    /// <summary>
    /// Días hábiles transcurridos desde la entrada a revisión. El caché (por día
    /// local de entrada) vale solo para una misma evaluación (mismo <paramref name="ahoraUtc"/>).
    /// </summary>
    public static async Task<int> DiasHabilesAsync(
        ICalendarioHabil calendario,
        DateTimeOffset entradaUtc,
        DateTimeOffset ahoraUtc,
        IDictionary<DateOnly, int> cache,
        CancellationToken ct)
    {
        var desde = FechaContable.HoyLocal(entradaUtc);
        var hasta = FechaContable.HoyLocal(ahoraUtc);
        if (cache.TryGetValue(desde, out var dias)) return dias;
        dias = await calendario.ContarDiasAsync(desde, hasta, ct);
        cache[desde] = dias;
        return dias;
    }
}
