using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Ports.Catalogos;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Compras.Infrastructure.Catalogos;

public sealed class CatalogoSatReadAdapter(CompartidoDbContext db) : ICatalogoSatReadPort
{
    public Task<ClaveSatLectura?> ObtenerUsoCfdiAsync(string clave, CancellationToken cancellationToken) =>
        db.UsosCfdi.AsNoTracking().Where(x => x.ClaveSat == clave)
            .Select(x => new ClaveSatLectura(x.Descripcion, x.Activa)).SingleOrDefaultAsync(cancellationToken);

    public Task<ClaveSatLectura?> ObtenerFormaPagoAsync(string clave, CancellationToken cancellationToken) =>
        db.FormasPago.AsNoTracking().Where(x => x.ClaveSat == clave)
            .Select(x => new ClaveSatLectura(x.Descripcion, x.Activa)).SingleOrDefaultAsync(cancellationToken);
}
