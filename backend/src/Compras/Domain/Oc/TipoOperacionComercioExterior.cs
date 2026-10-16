namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Sentido de la operación de comercio exterior de la OC. Persistido como
/// <c>smallint</c>. PLATFORM-TODO(F1-COM-06): supuesto Vilo, confirmar con
/// la lista oficial de Millet (valores y nombres).
/// </summary>
public enum TipoOperacionComercioExterior : short
{
    Importacion = 0,
    Exportacion = 1,
}
