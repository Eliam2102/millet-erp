using Millet.CuentasPorPagar.Domain.Ports.Administracion;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// C1.5b: sucursal de la póliza de los gastos y pasivos de un empleado (viáticos y tarjeta corporativa), que no guardan sucursal.
/// Sale del catálogo de empleados (<c>Empleado.SucursalId</c>, 1:1 de RH). Sin puerto, sin empleado o sin sucursal → null: el evento
/// viaja igual y Contabilidad lo deja en la bandeja con DatoFaltante · SucursalId.
/// </summary>
internal static class SucursalEmpleado
{
    public static async Task<Guid?> DeAsync(IEmpleadoReadPort? empleados, Guid empleadoId, CancellationToken ct) =>
        empleados is null || empleadoId == Guid.Empty ? null : (await empleados.ObtenerAsync(empleadoId, ct))?.SucursalId;
}
