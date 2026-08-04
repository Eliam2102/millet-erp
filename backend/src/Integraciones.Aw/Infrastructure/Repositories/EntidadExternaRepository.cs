using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;

namespace Millet.Integraciones.Aw.Infrastructure.Repositories;

/// <summary>
/// Implementación productiva de <see cref="IEntidadExternaRepository"/>
/// que lee de <see cref="IntegracionesAwDbContext"/>.
/// </summary>
public sealed class EntidadExternaRepository : IEntidadExternaRepository
{
    private readonly IntegracionesAwDbContext _db;

    public EntidadExternaRepository(IntegracionesAwDbContext db)
    {
        _db = db;
    }

    public Task<EntidadExterna?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => _db.EntidadesExternas.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<EntidadExterna?> GetByReferenciaAsync(
        TipoEntidad tipoEntidad,
        string referenciaExterna,
        Guid empresaId,
        CancellationToken cancellationToken)
        => _db.EntidadesExternas.FirstOrDefaultAsync(
            e => e.TipoEntidad == tipoEntidad
              && e.ReferenciaExterna == referenciaExterna
              && e.EmpresaId == empresaId,
            cancellationToken);

    public Task<EntidadExterna?> GetByIdCrossEmpresaAsync(
        Guid id,
        CancellationToken cancellationToken)
        => _db.EntidadesExternas.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
}
