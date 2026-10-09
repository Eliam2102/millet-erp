namespace Millet.CuentasPorPagar.Application.Reportes.Comun;

/// <summary>
/// Buckets estándar de antigüedad de saldos del módulo CxP (F8-PR1).
/// Coincide con el patrón del Portal Millet legacy + práctica común
/// de antigüedad de cuentas por pagar.
/// </summary>
public static class BucketsAntiguedad
{
    public const string BucketPorVencer = "por_vencer";
    public const string Bucket0a30      = "b0_30";
    public const string Bucket31a60     = "b31_60";
    public const string Bucket61a90     = "b61_90";
    public const string BucketMas90     = "bMas90";

    /// <summary>
    /// Devuelve el código del bucket al que pertenece una factura
    /// según los días entre <paramref name="fechaVencimiento"/> y
    /// <paramref name="fechaCorte"/>.
    ///
    /// <para>
    /// Días &gt; 0 = factura vencida. Días &lt; 0 = factura todavía en
    /// plazo (por vencer). Días == 0
    /// = vence hoy (bucket 0-30).
    /// </para>
    /// </summary>
    public static string CalcularBucket(DateOnly fechaVencimiento, DateOnly fechaCorte)
    {
        var dias = fechaCorte.DayNumber - fechaVencimiento.DayNumber;
        return dias switch
        {
            < 0 => BucketPorVencer,
            <= 30 => Bucket0a30,
            <= 60 => Bucket31a60,
            <= 90 => Bucket61a90,
            _     => BucketMas90,
        };
    }
}
