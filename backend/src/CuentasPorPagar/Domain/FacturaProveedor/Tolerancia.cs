using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Domain.FacturaProveedor;

/// <summary>
/// Tolerancia de conciliación con OC (§3.bis.3 del 01-diseno).
/// Snapshot persistido en cada <see cref="FacturaProveedor"/> al
/// capturarse: aunque el master del proveedor cambie después, la
/// validación histórica permanece estable.
///
/// <para>
/// La aritmética se hace sobre el monto absoluto de la diferencia
/// <c>|total_factura - total_oc|</c>:
/// <list type="bullet">
///   <item><see cref="ToleranciaTipo.MontoAbsoluto"/> — pasa si la
///         diferencia &lt;= <see cref="Valor"/> (MXP).</item>
/// </list>
/// </para>
/// </summary>
public sealed record Tolerancia(ToleranciaTipo Tipo, decimal Valor)
{
    public static Tolerancia MontoAbsoluto(decimal monto)
    {
        if (monto < 0)
            throw new BusinessRuleException(
                "TOLERANCIA_VALOR_NEGATIVO",
                "El monto absoluto de la tolerancia no puede ser negativo.");
        return new Tolerancia(ToleranciaTipo.MontoAbsoluto, monto);
    }

    /// <summary>
    /// True si la <paramref name="diferencia"/> contra
    /// <paramref name="totalOc"/> está dentro de tolerancia (§3.bis.3).
    /// </summary>
    public bool Pasa(decimal diferencia, decimal totalOc)
    {
        var diff = Math.Abs(diferencia);
        return Tipo switch
        {
            ToleranciaTipo.MontoAbsoluto => diff <= Valor,
            _ => false,
        };
    }
}

public enum ToleranciaTipo
{
    MontoAbsoluto = 1,
    // 2 reservado: Porcentaje retirado (86e3tab1k); ninguna factura lo usa.
}
