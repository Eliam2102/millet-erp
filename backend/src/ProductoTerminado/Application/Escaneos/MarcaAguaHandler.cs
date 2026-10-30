using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Infrastructure.Persistence;

namespace Millet.ProductoTerminado.Application.Escaneos;

public sealed class MarcaAguaHandler(ProductoTerminadoDbContext db) : IRequestHandler<ObtenerMarcaAguaEscaneosQuery, DateTime?>
{
    public Task<DateTime?> Handle(ObtenerMarcaAguaEscaneosQuery q, CancellationToken cancellationToken) =>
        db.Escaneos.Where(x => x.EmpresaId == q.EmpresaId && x.Origen == q.Origen)
            .MaxAsync(x => (DateTime?)x.RegistradoAt, cancellationToken);
}
