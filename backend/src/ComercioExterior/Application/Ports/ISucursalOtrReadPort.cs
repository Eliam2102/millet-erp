namespace Millet.ComercioExterior.Application.Ports;

public interface ISucursalOtrReadPort
{
    Task<Guid?> ResolverAsync(string claveAw, CancellationToken cancellationToken);
}
