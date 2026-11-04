using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;

namespace Millet.Tesoreria.Infrastructure.Adapters;

/// <summary>Adapter de <see cref="IEmpresaActaReadPort"/> sobre <c>compartido.empresas</c>, igual que <see cref="ClienteReadPortAdapter"/>.</summary>
public sealed class EmpresaActaReadPortAdapter(CompartidoDbContext db) : IEmpresaActaReadPort
{
    public Task<EmpresaActaDto?> ObtenerAsync(Guid empresaId, CancellationToken cancellationToken) =>
        db.Empresas.AsNoTracking().Where(e => e.Id == empresaId)
            .Select(e => new EmpresaActaDto(e.RazonSocial, e.Rfc))
            .FirstOrDefaultAsync(cancellationToken);
}
