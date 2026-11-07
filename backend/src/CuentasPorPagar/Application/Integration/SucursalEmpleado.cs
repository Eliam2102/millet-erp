using Millet.CuentasPorPagar.Domain.Ports.Administracion;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// C1.5b: sucursal de la póliza de los gastos y pasivos de un empleado (viáticos y tarjeta corporativa), que no guardan sucursal.
/// Sale del catálogo de empleados (<c>Empleado.SucursalId</c>, 1:1 de RH). Sin puerto, sin empleado o sin sucursal → null: el evento
/// viaja igual y Contabilidad lo deja en la bandeja con DatoFaltante · SucursalId.
/// </summary>
internal static class SucursalEmpleado
{
    /// <remarks>
    /// H1 (86e3z5w25): en tarjeta, <c>UsuarioQueUsoId</c> llega del selector de usuarios; si el id no es de un empleado se busca el
    /// empleado vinculado a ese usuario. Así la sucursal del gasto (y el alcance de su área o equipo) no se pierde.
    /// </remarks>
    public static async Task<Guid?> DeAsync(IEmpleadoReadPort? empleados, Guid empleadoId, CancellationToken ct)
    {
        if (empleados is null || empleadoId == Guid.Empty) return null;
        var empleado = await empleados.ObtenerAsync(empleadoId, ct) ?? await empleados.ObtenerPorUsuarioAsync(empleadoId, ct);
        return empleado?.SucursalId is { } s && s != Guid.Empty ? s : null;
    }
}
