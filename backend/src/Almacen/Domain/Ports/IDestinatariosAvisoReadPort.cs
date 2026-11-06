namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura hacia Identidad (adapter hospedado allí, igual que <see cref="IUsuarioReadPort"/>): usuarios activos de la
/// empresa que tienen un permiso por rol (o excepción concedida). Con <paramref name="sucursalId"/> solo los asignados a esa sucursal
/// o con alcance corporativo. Así el destinatario de un aviso es un rol configurable, no una persona fija.
/// </summary>
public interface IDestinatariosAvisoReadPort
{
    Task<IReadOnlyList<Guid>> UsuariosConPermisoAsync(Guid empresaId, string permiso, Guid? sucursalId, CancellationToken cancellationToken);
}
