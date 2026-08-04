using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Value object inmutable con el indicador de impuestos aplicado a una
/// línea de OC. Hereda del régimen fiscal del proveedor + régimen del
/// artículo (decisión C3); el motor real entra en F3-PR2. En F2-PR1 (motor
/// v0) usamos un indicador genérico fijo <c>IVA16</c>.
///
/// El código es opaco para el agregado — solo lo persiste como
/// <c>varchar(40)</c> snapshot. El motor de impuestos interpreta el código
/// para calcular tasas.
/// </summary>
public readonly record struct IndicadorImpuestos
{
    public string Codigo { get; }

    public IndicadorImpuestos(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
        {
            throw new BusinessRuleException(
                "INDICADOR_IMPUESTOS_VACIO",
                "El indicador de impuestos no puede ser vacío.");
        }

        if (codigo.Length > 40)
        {
            throw new BusinessRuleException(
                "INDICADOR_IMPUESTOS_DEMASIADO_LARGO",
                "El indicador de impuestos no puede exceder 40 caracteres.");
        }

        Codigo = codigo;
    }

    /// <summary>Default v0: IVA 16% general (motor real en F3-PR2).</summary>
    public static IndicadorImpuestos Iva16Default => new("IVA16");

    public override string ToString() => Codigo;
}
