namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura hacia Finanzas (futuro) para verificar si un periodo
/// contable está abierto. Lo invocan TODOS los handlers de movimientos
/// con <c>fecha_movimiento</c> retroactiva — si el periodo está cerrado,
/// rechazan con 422 (cuidado §6.1 del 04-cuidados-infra).
///
/// <para>
/// Almacén también mantiene su propia tabla <c>almacen.periodos_cerrados</c>
/// (F8-PR2) sincronizada con el calendario fiscal. Este puerto sirve para
/// validación cross-módulo cuando Finanzas exista; mientras tanto la
/// implementación NoOp siempre retorna "abierto".
/// </para>
/// </summary>
public interface IPeriodoContableReadPort
{
    Task<bool> EstaAbiertoAsync(
        int año,
        int mes,
        CancellationToken cancellationToken);
}
