using Microsoft.Extensions.Options;

namespace Millet.CuentasPorCobrar.Application.Reportes.Comun;

/// <summary>
/// Configuración de los buckets de antigüedad de CxC (CXC-PR6,
/// levantamiento §1.2: buckets CONFIGURABLES — nacional observado
/// 0-15/16-30/31-60/61-90/91+; internacional/SAP maneja otros cortes).
/// Se bindea de <c>CuentasPorCobrar:Reportes</c>; cambiar los cortes es
/// tocar configuración, no código.
/// </summary>
public sealed class ReportesCxcOptions
{
    public const string SectionName = "CuentasPorCobrar:Reportes";

    /// <summary>
    /// Límites superiores (días vencidos) de cada bucket; el último bucket
    /// es abierto (+). OJO: sin default aquí — el ConfigurationBinder
    /// CONCATENA los elementos del appsettings sobre el array preexistente
    /// (incidente 2026-07-14: [15,30,60,90] duplicado → límites no
    /// ascendentes → 500 en /cartera/antiguedad). El default vive en
    /// <see cref="BucketsAntiguedadCxc.LimitesDefault"/>.
    /// </summary>
    public int[] BucketLimites { get; set; } = [];
}

/// <summary>
/// Cálculo de buckets de antigüedad sobre días VENCIDOS (a diferencia de
/// CxP, aquí "saldo vencido = suma de los buckets" — levantamiento §2.2 —
/// así que lo no vencido va en la columna aparte <c>por_vencer</c>).
/// </summary>
public sealed class BucketsAntiguedadCxc
{
    public const string PorVencer = "por_vencer";

    /// <summary>Cortes default (nacional §1.2) cuando la config no define ninguno.</summary>
    public static readonly int[] LimitesDefault = [15, 30, 60, 90];

    private readonly int[] _limites;

    public BucketsAntiguedadCxc(IOptions<ReportesCxcOptions> options)
        : this(options.Value.BucketLimites is { Length: > 0 } limites ? limites : LimitesDefault) { }

    public BucketsAntiguedadCxc(int[] limites)
    {
        if (limites.Length == 0 || limites.Zip(limites.Skip(1)).Any(p => p.First >= p.Second))
            throw new ArgumentException("Los límites de bucket deben ser ascendentes y no vacíos.", nameof(limites));
        _limites = limites;
    }

    /// <summary>Keys de los buckets vencidos, en orden (b1_15, b16_30, ..., bMas90).</summary>
    public IReadOnlyList<string> Keys =>
        _limites.Select((lim, i) => Key(i)).Append(Key(_limites.Length)).ToList();

    /// <summary>Labels legibles (1-15, 16-30, ..., +90 días).</summary>
    public IReadOnlyList<string> Labels =>
        _limites.Select((lim, i) => i == 0 ? $"1-{lim} días" : $"{_limites[i - 1] + 1}-{lim} días")
            .Append($"+{_limites[^1]} días")
            .ToList();

    /// <summary>
    /// Bucket de una factura según días vencidos a la fecha de corte.
    /// Días ≤ 0 (aún en plazo o vence hoy) → <see cref="PorVencer"/>.
    /// </summary>
    public string Calcular(DateOnly fechaVencimiento, DateOnly fechaCorte)
    {
        var dias = fechaCorte.DayNumber - fechaVencimiento.DayNumber;
        if (dias <= 0) return PorVencer;

        for (var i = 0; i < _limites.Length; i++)
        {
            if (dias <= _limites[i]) return Key(i);
        }
        return Key(_limites.Length);
    }

    private string Key(int index) => index switch
    {
        0 => $"b1_{_limites[0]}",
        _ when index == _limites.Length => $"bMas{_limites[^1]}",
        _ => $"b{_limites[index - 1] + 1}_{_limites[index]}",
    };
}
