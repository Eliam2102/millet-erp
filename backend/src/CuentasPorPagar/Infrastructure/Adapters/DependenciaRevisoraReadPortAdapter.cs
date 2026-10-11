using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
namespace Millet.CuentasPorPagar.Infrastructure.Adapters;

public sealed class DependenciaRevisoraReadPortAdapter(CompartidoDbContext db) : IDependenciaRevisoraReadPort
{
    public Task<DependenciaRevisoraDto?> ObtenerAsync(Guid dependenciaId, CancellationToken cancellationToken) =>
        db.Departamentos.AsNoTracking().Where(d => d.Id == dependenciaId)
            .Select(d => new DependenciaRevisoraDto(d.Id, d.Clave, d.Nombre, d.Estatus == EstatusCatalogo.Activo))
            .FirstOrDefaultAsync(cancellationToken);
    public async Task<IReadOnlyList<DependenciaRevisoraDto>> ListarAsync(CancellationToken cancellationToken) =>
        await db.Departamentos.AsNoTracking().Where(d => d.Estatus == EstatusCatalogo.Activo).OrderBy(d => d.Nombre)
            .Select(d => new DependenciaRevisoraDto(d.Id, d.Clave, d.Nombre, true)).ToListAsync(cancellationToken);
}
