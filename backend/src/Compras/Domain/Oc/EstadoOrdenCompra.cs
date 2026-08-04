namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Estados del ciclo de vida de una <see cref="OrdenCompra"/>. State machine
/// del diseño §5.1 (7 estados). Las transiciones permitidas se documentan
/// en §5.3 y se aplican vía métodos del agregado (EnviarAAutorizacion,
/// Autorizar, Rechazar, Cancelar) — F1-PR1 solo introduce el estado
/// inicial <see cref="Borrador"/>.
///
/// Persistido como <c>smallint</c> con CHECK constraint
/// (<c>estado BETWEEN 0 AND 6</c>); ver diseño §10.1.
/// </summary>
public enum EstadoOrdenCompra : short
{
    Borrador = 0,
    EnAutorizacionJefeCompras = 1,
    EnAutorizacionDireccion = 2,
    Autorizada = 3,
    Cerrada = 4,
    Cancelada = 5,
    Rechazada = 6,
}
