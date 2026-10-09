using Millet.Almacen.Domain.Conteos;

namespace Millet.Almacen.Domain.Ports;

public interface IConteoUmbralesProvider
{
    Task<ConteoUmbrales> ObtenerAsync(CancellationToken cancellationToken);
}
