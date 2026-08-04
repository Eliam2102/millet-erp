using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Value object inmutable que representa un descuento sobre una
/// <see cref="LineaOrdenCompra"/> (diseño §4.8). Aplicación:
///
/// <list type="bullet">
///   <item><see cref="DescuentoTipo.Porcentaje"/>: <c>Valor</c> es 0–100;
///         se aplica como <c>cantidad * precio * (Valor/100)</c>.</item>
///   <item><see cref="DescuentoTipo.Monto"/>: <c>Valor</c> es el monto
///         absoluto a descontar del subtotal bruto de la línea
///         (cantidad * precio). Debe ser ≥ 0.</item>
/// </list>
///
/// La aplicación sobre el subtotal bruto la hace <see cref="Aplicar"/>;
/// el motor de impuestos (v0 IVA 16%) opera sobre el subtotal post-descuento.
/// </summary>
public readonly record struct DescuentoLinea
{
    public DescuentoTipo Tipo { get; }
    public decimal Valor { get; }

    public DescuentoLinea(DescuentoTipo tipo, decimal valor)
    {
        if (tipo == DescuentoTipo.Porcentaje && (valor < 0m || valor > 100m))
        {
            throw new BusinessRuleException(
                "DESCUENTO_PORCENTAJE_INVALIDO",
                $"El descuento porcentaje debe estar entre 0 y 100; recibido: {valor}.");
        }

        if (tipo == DescuentoTipo.Monto && valor < 0m)
        {
            throw new BusinessRuleException(
                "DESCUENTO_MONTO_INVALIDO",
                $"El descuento monto no puede ser negativo; recibido: {valor}.");
        }

        Tipo = tipo;
        Valor = valor;
    }

    public static DescuentoLinea Cero => new(DescuentoTipo.Monto, 0m);

    /// <summary>
    /// Aplica el descuento sobre <paramref name="subtotalBruto"/> y devuelve
    /// el monto descontado (siempre ≥ 0). El subtotal post-descuento es
    /// <c>subtotalBruto - resultado</c>.
    /// </summary>
    public decimal Aplicar(decimal subtotalBruto)
    {
        if (subtotalBruto <= 0m) return 0m;

        return Tipo switch
        {
            DescuentoTipo.Porcentaje => Math.Round(subtotalBruto * (Valor / 100m), 2, MidpointRounding.ToEven),
            DescuentoTipo.Monto => Math.Min(Valor, subtotalBruto),
            _ => 0m,
        };
    }
}
