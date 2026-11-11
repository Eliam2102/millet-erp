using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.Liberacion;

/// <summary>
/// M-239: cuántas firmas pide una excepción de crédito según su monto en MXN. Un nivel hasta el umbral (Crédito y
/// Cobranza); arriba, una segunda firma de Dirección (decisión M-085). Sin reglas se aplica la de M-085: arriba de
/// <see cref="ExcesoCredito.TopeCreditoYCobranza"/> pide dos firmas. Misma forma que las firmas de cancelación (M-235).
/// </summary>
public sealed class ReglaFirmaExcepcionCredito : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    /// <summary>Monto en MXN arriba del cual aplica la regla.</summary>
    public decimal UmbralMxn { get; private set; }

    /// <summary>1 o 2.</summary>
    public short FirmasRequeridas { get; private set; }

    private ReglaFirmaExcepcionCredito() { }

    public static ReglaFirmaExcepcionCredito Crear(Guid empresaId, decimal umbralMxn, short firmasRequeridas)
    {
        if (umbralMxn < 0)
            throw new BusinessRuleException("AC_REGLA_UMBRAL_INVALIDO", "El umbral no puede ser negativo.");
        if (firmasRequeridas is < 1 or > 2)
            throw new BusinessRuleException("AC_REGLA_FIRMAS_INVALIDAS", "Una excepción pide una o dos firmas.");
        return new ReglaFirmaExcepcionCredito
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            UmbralMxn = Math.Round(umbralMxn, 2, MidpointRounding.AwayFromZero),
            FirmasRequeridas = firmasRequeridas,
        };
    }

    /// <summary>
    /// Firmas que pide una excepción: la mayor de las reglas que le aplican (monto &gt; umbral); sin reglas, la de M-085.
    /// Como el tope de M-085, solo aplica a montos en MXN; en otra moneda basta una firma hasta que Crédito y Cobranza
    /// diga cómo convertir (supuesto a validar).
    /// </summary>
    public static short FirmasPara(IReadOnlyCollection<ReglaFirmaExcepcionCredito> reglas, string moneda, decimal monto)
    {
        if (!string.Equals(moneda, "MXN", StringComparison.OrdinalIgnoreCase)) return 1;
        if (reglas.Count == 0) return monto > ExcesoCredito.TopeCreditoYCobranza ? (short)2 : (short)1;
        return reglas.Where(r => monto > r.UmbralMxn).Select(r => r.FirmasRequeridas).DefaultIfEmpty((short)1).Max();
    }
}
