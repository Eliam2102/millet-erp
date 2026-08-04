using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Value object inmutable que representa el descuento global a nivel
/// cabecera de la <see cref="OrdenCompra"/> (diseño §4.8). Mismo shape que
/// <see cref="DescuentoLinea"/> pero semántico distinto: se aplica sobre
/// la suma de subtotales de línea (post-descuento por línea) y antes de
/// los gastos adicionales (ver §4.9 fórmula <c>TotalesOC</c>).
/// </summary>
public readonly record struct DescuentoGlobal
{
    public DescuentoTipo Tipo { get; }
    public decimal Valor { get; }

    public DescuentoGlobal(DescuentoTipo tipo, decimal valor)
    {
        if (tipo == DescuentoTipo.Porcentaje && (valor < 0m || valor > 100m))
        {
            throw new BusinessRuleException(
                "DESCUENTO_GLOBAL_PORCENTAJE_INVALIDO",
                $"El descuento global porcentaje debe estar entre 0 y 100; recibido: {valor}.");
        }

        if (tipo == DescuentoTipo.Monto && valor < 0m)
        {
            throw new BusinessRuleException(
                "DESCUENTO_GLOBAL_MONTO_INVALIDO",
                $"El descuento global monto no puede ser negativo; recibido: {valor}.");
        }

        Tipo = tipo;
        Valor = valor;
    }

    public static DescuentoGlobal Cero => new(DescuentoTipo.Monto, 0m);

    /// <summary>
    /// Aplica el descuento global sobre <paramref name="subtotalAntesDescuento"/>
    /// (suma de subtotales de línea ya con descuento por línea aplicado) y
    /// devuelve el monto descontado (siempre ≥ 0).
    /// </summary>
    public decimal Aplicar(decimal subtotalAntesDescuento)
    {
        if (subtotalAntesDescuento <= 0m) return 0m;

        return Tipo switch
        {
            DescuentoTipo.Porcentaje => Math.Round(subtotalAntesDescuento * (Valor / 100m), 2, MidpointRounding.ToEven),
            DescuentoTipo.Monto => Math.Min(Valor, subtotalAntesDescuento),
            _ => 0m,
        };
    }
}
