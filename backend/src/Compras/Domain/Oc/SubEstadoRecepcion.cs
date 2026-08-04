namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Sub-estado materializado de la dimensión Recepción (diseño §3.bis.2,
/// §5.2). Avanza independientemente cuando la OC está
/// <see cref="EstadoOrdenCompra.Autorizada"/>; los handlers de los listeners
/// del módulo Almacén lo actualizan según las recepciones registradas.
///
/// Persistido como <c>smallint</c> con CHECK constraint (0..2).
/// </summary>
public enum SubEstadoRecepcion : short
{
    SinRecepcion = 0,
    Parcial = 1,
    Completa = 2,
}
