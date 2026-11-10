using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Contabilidad.Application.Ports;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>K10.8: RFC de la empresa para los archivos de contabilidad electrónica.</summary>
public sealed class EmpresaContabilidadReadAdapter(CompartidoDbContext db) : IEmpresaContabilidadReadPort
{
    public async Task<string?> RfcAsync(Guid empresaId, CancellationToken ct)
    {
        var rfc = await db.Empresas.AsNoTracking().Where(e => e.Id == empresaId).Select(e => e.Rfc).FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(rfc) ? null : rfc;
    }
}
