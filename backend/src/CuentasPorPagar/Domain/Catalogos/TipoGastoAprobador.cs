namespace Millet.CuentasPorPagar.Domain.Catalogos;

/// <summary>
/// Tipo de gasto al que aplica un <c>AprobadorLimite</c> (§5.5 del
/// 00-levantamiento, F7-PR3). Define el ámbito de autorización por
/// usuario en el catálogo de aprobadores con límites.
/// </summary>
public enum TipoGastoAprobador
{
    ReembolsoCajaChica          = 1,
    Viaticos                    = 2,
    TarjetaCreditoEmpresarial   = 3,
    OtrosSinOc                  = 4,
}
