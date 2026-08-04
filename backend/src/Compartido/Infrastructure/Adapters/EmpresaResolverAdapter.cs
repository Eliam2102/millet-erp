using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Application.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Compartido.Infrastructure.Adapters;

/// <summary>
/// Implementación productiva de <see cref="IEmpresaResolverPort"/> que
/// lee de <see cref="CompartidoDbContext"/>. Scoped para reusar el
/// DbContext del request actual.
///
/// <para>
/// Usa <see cref="ICurrentEmpresaContext.Bypass"/> porque la consulta es
/// por RFC, no por la empresa actual del JWT. Si no se hace bypass, el
/// global query filter de empresa NO afecta a la query (porque la
/// entidad <c>Empresa</c> no implementa <c>IPerteneceAEmpresa</c>), pero
/// usamos bypass por defensa explícita y consistencia con otros adapters
/// cross-context (ver <c>BootstrapSuperAdminHostedService</c>).
/// </para>
/// </summary>
public sealed class EmpresaResolverAdapter : IEmpresaResolverPort
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public EmpresaResolverAdapter(CompartidoDbContext db, ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<EmpresaResolution?> ResolveByRfcAsync(
        string rfc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rfc))
        {
            return null;
        }

        using var bypass = _empresaContext.Bypass();

        var empresa = await _db.Empresas
            .AsNoTracking()
            .Where(e => e.Rfc == rfc)
            .Select(e => new EmpresaResolution(e.Id, e.Rfc, e.RazonSocial))
            .FirstOrDefaultAsync(cancellationToken);

        return empresa;
    }
}
