namespace Millet.CuentasPorCobrar.Domain.Ports.Administracion;

/// <summary>H-U41: nombres de usuario por id (quién propuso y quién resolvió una propuesta de aplicación).</summary>
public interface IUsuarioNombreReadPort
{
    Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(
        IReadOnlyCollection<Guid> usuarioIds, CancellationToken cancellationToken);
}
