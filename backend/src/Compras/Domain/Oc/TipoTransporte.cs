namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Medio de transporte del embarque de comercio exterior. Persistido como
/// <c>smallint</c>. PLATFORM-TODO(F1-COM-06): supuesto Vilo, confirmar con
/// la lista oficial de Millet (valores y nombres).
/// </summary>
public enum TipoTransporte : short
{
    Maritimo = 0,
    Aereo = 1,
    Terrestre = 2,
}
