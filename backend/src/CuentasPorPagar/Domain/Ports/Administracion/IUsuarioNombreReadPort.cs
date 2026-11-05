namespace Millet.CuentasPorPagar.Domain.Ports.Administracion;

/// <summary>Nombres de usuario por id (para mostrar responsable y quién decidió).</summary>
public interface IUsuarioNombreReadPort
{
    Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(
        IReadOnlyCollection<Guid> usuarioIds, CancellationToken cancellationToken);
}
