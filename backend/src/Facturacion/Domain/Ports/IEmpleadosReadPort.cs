namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Lectura del catálogo de empleados del ERP (<c>compartido.empleados</c>, el mismo que se publica a Planta Pintura como
/// <c>dbo.pinturas_empleado</c>). La ingesta de Planta Pintura valida con él las claves de capturista y vendedor
/// (script 16 v4).
/// </summary>
public interface IEmpleadosReadPort
{
    /// <summary>True si existe un empleado con esa clave (activo o dado de baja; se compara sin espacios).</summary>
    Task<bool> ExisteClaveAsync(string clave, CancellationToken cancellationToken);
}
