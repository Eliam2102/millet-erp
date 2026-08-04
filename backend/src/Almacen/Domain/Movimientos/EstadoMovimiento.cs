namespace Millet.Almacen.Domain.Movimientos;

/// <summary>
/// Ciclo de vida del movimiento (01-diseno §4.2):
/// <list type="bullet">
///   <item><see cref="Borrador"/> — capturado, no firme; no afecta saldo.</item>
///   <item><see cref="Validado"/> — pasó validación (opcional);
///   editable, no afecta saldo.</item>
///   <item><see cref="Registrado"/> — firme. Inmutable.
///   <b>Actualiza saldo en la misma transacción</b> (trigger PG).</item>
///   <item><see cref="Cancelado"/> — solo desde Borrador.</item>
/// </list>
/// </summary>
public enum EstadoMovimiento : short
{
    Borrador = 0,
    Validado = 1,
    Registrado = 2,
    Cancelado = 3,
}
