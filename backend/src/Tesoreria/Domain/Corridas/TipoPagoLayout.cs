namespace Millet.Tesoreria.Domain.Corridas;

/// <summary>
/// Tipo de pago para layouts bancarios (§4.5, T9.3, 86e3ke5ck):
/// nacional (SPEI) o internacional.
/// </summary>
public enum TipoPagoLayout : short
{
    NacionalSpei = 1,
    Internacional = 2,
}
