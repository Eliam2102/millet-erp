namespace Millet.CuentasPorCobrar.Domain.Cobranza;

/// <summary>Estado de una promesa de pago frente a los pagos aplicados a la factura (M-345).</summary>
public enum EstadoPromesa : short
{
    /// <summary>La fecha prometida aún no pasa y falta pagar.</summary>
    Vigente = 1,
    Cumplida = 2,
    /// <summary>Venció la fecha y se pagó algo, pero menos de lo prometido.</summary>
    Parcial = 3,
    /// <summary>Venció la fecha sin ningún pago.</summary>
    Incumplida = 4,
}

/// <summary>Pago aplicado a la factura (solo pagos vigentes, no revertidos).</summary>
public sealed record PagoAplicado(DateOnly Fecha, decimal Importe);

public sealed record ResultadoPromesa(EstadoPromesa Estado, decimal Pagado, decimal Faltante);

/// <summary>
/// Compara una promesa contra los pagos aplicados a la factura. Cuentan los pagos desde el día de la
/// promesa hasta la fecha prometida, ambos incluidos; los anteriores o posteriores no. Una promesa
/// cubierta se da por cumplida aunque la fecha no haya llegado; si falta pagar, sigue vigente hasta
/// la fecha prometida y después queda parcial (algo pagado) o incumplida (nada pagado).
/// </summary>
public static class CumplimientoPromesa
{
    public static ResultadoPromesa Evaluar(
        decimal montoPrometido, DateOnly fechaPrometida, DateOnly desde, IEnumerable<PagoAplicado> pagos, DateOnly hoy)
    {
        var pagado = pagos.Where(p => p.Fecha >= desde && p.Fecha <= fechaPrometida && p.Importe > 0).Sum(p => p.Importe);
        var faltante = Math.Max(0m, montoPrometido - pagado);
        var estado = faltante == 0m ? EstadoPromesa.Cumplida
            : hoy <= fechaPrometida ? EstadoPromesa.Vigente
            : pagado > 0m ? EstadoPromesa.Parcial
            : EstadoPromesa.Incumplida;
        return new ResultadoPromesa(estado, pagado, faltante);
    }
}
