namespace Millet.CuentasPorPagar.Domain.Ports.Administracion;

/// <summary>
/// Puerto de lectura del catálogo de empleados (vive en
/// Administración/DatosMaestros). CxP lo consume para resolver titulares
/// de TC, autores de comprobaciones de gastos y aprobadores de viáticos
/// (§6.1 del 01-diseno).
///
/// <para>
/// La propiedad <see cref="EmpleadoDto.PuestoId"/> es requerida para la
/// política de viáticos por puesto + tipo de destino (A18 del diseño).
/// </para>
/// </summary>
public interface IEmpleadoReadPort
{
    Task<EmpleadoDto?> ObtenerAsync(Guid empleadoId, CancellationToken cancellationToken);

    /// <summary>
    /// Resuelve el empleado ACTIVO vinculado a un usuario de Identidad
    /// (correlación <c>Empleado.UsuarioId</c>, decisión D5 del doc 10 de
    /// Administración). Lo usan las firmas de viáticos para comparar al
    /// usuario autenticado contra <c>JefeDirectoId</c> (que es un
    /// empleado del catálogo). <c>null</c> = el usuario no está vinculado
    /// a ningún empleado activo.
    /// </summary>
    Task<EmpleadoDto?> ObtenerPorUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken);

    /// <summary>
    /// Resuelve id → empleado en batch para las bandejas. Implementación por defecto
    /// vía <see cref="ObtenerAsync"/>; el adapter productivo la sobreescribe con una consulta.
    /// </summary>
    async Task<IReadOnlyDictionary<Guid, EmpleadoDto>> ObtenerPorIdsAsync(
        IReadOnlyCollection<Guid> empleadoIds, CancellationToken cancellationToken)
    {
        var resultado = new Dictionary<Guid, EmpleadoDto>();
        foreach (var id in empleadoIds)
        {
            var e = await ObtenerAsync(id, cancellationToken);
            if (e is not null) resultado[id] = e;
        }
        return resultado;
    }
}

public sealed record EmpleadoDto(
    Guid Id,
    Guid EmpresaId,
    string Nombre,
    string Email,
    Guid? PuestoId,
    Guid? SucursalId,
    bool Activo);
