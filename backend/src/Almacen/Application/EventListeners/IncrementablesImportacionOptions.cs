namespace Millet.Almacen.Application.EventListeners;

/// <summary>
/// F1-ALM-13 (Contabilidad, Fernando Alejos 4-nov): los incrementables de importación de Comercio Exterior (fletes, aranceles, etc.) NO
/// suben el valor del inventario «por ahora; para una versión posterior». Se registran a su cuenta de gasto al registrar el pasivo de
/// las facturas en CxP. Con <see cref="AlInventario"/> = false (por omisión) todo el ajuste va a gasto de importación y el saldo de
/// Almacén no cambia; true reactiva el reparto a inventario de Millet 26-oct (Opción A) cuando se libere esa versión.
/// </summary>
public sealed class IncrementablesImportacionOptions
{
    public const string SectionName = "ComercioExterior:IncrementablesImportacion";

    public bool AlInventario { get; set; }
}
