using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Millet.ProductoTerminado.Infrastructure.Persistence;
namespace Millet.Api.Web;
/// <summary>PT01: genera migraciones sin arrancar API, workers ni seed.</summary>
public sealed class PT01ProductoTerminadoDesignTimeFactory : IDesignTimeDbContextFactory<ProductoTerminadoDbContext>
{
    public ProductoTerminadoDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<ProductoTerminadoDbContext>()
            .UseNpgsql(DesignTimeConexion.Valor).UseSnakeCaseNamingConvention().Options, new DesignTimeEmpresa());
}
