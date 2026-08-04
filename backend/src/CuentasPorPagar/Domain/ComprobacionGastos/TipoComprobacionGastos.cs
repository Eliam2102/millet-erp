namespace Millet.CuentasPorPagar.Domain.ComprobacionGastos;

/// <summary>
/// Discriminador de <see cref="ComprobacionGastos"/> según §7.4 del
/// 00-levantamiento. F7-PR1 implementa <see cref="ReembolsoCajaChica"/>;
/// los otros tipos entran en F7-PR2/PR3.
/// </summary>
public enum TipoComprobacionGastos
{
    ReembolsoCajaChica  = 1,
    GastosAduanales     = 2,
    Viaticos            = 3,
    TarjetaCredito      = 4,
}
