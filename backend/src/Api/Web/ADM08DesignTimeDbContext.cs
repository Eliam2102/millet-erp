using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Millet.CentrosCosto.Infrastructure.Persistence;
namespace Millet.Api.Web;
/// <summary>ADM08: genera migraciones sin arrancar API, workers ni seed.</summary>
public sealed class ADM08CentrosCostoDesignTimeFactory : IDesignTimeDbContextFactory<CentrosCostoDbContext>
{
    public CentrosCostoDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<CentrosCostoDbContext>()
            .UseNpgsql(DesignTimeConexion.Valor).UseSnakeCaseNamingConvention().Options, new DesignTimeEmpresa());
}
