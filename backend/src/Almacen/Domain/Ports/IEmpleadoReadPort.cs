namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura hacia Administración para obtener datos de
/// empleado. Lo usan los handlers de conteos (responsables) y
/// devoluciones internas (solicitante).
///
/// <para>
/// La <b>persona destinataria de una salida</b> NO se resuelve por aquí: es
/// un usuario de Identidad (el selector la toma de <c>identidad.usuarios</c>),
/// y su nombre se resuelve vía <see cref="IUsuarioReadPort"/> (ADR-0042).
/// Adapter productivo:
/// <c>Compartido.Infrastructure.PublicAdapters.EmpleadoReadAdapter</c>
/// sobre <c>compartido.empleados</c> (ADM-PR2, registrado en Program.cs).
/// </para>
/// </summary>
public interface IEmpleadoReadPort
{
    Task<EmpleadoLectura?> ObtenerAsync(Guid empleadoId, CancellationToken cancellationToken);
}

public sealed record EmpleadoLectura(
    Guid Id,
    string NombreCompleto,
    Guid EmpresaId,
    Guid? DepartamentoId,
    bool EsActivo);
