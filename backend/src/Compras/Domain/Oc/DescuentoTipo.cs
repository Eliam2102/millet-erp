namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Tipo de descuento aplicado en línea o cabecera (diseño §4.8). Persistido
/// como <c>smallint</c>.
/// </summary>
public enum DescuentoTipo : short
{
    Porcentaje = 0,
    Monto = 1,
}
