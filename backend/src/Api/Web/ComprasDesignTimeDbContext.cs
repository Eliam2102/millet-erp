using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Millet.Compras.Infrastructure;

namespace Millet.Api.Web;

/// <summary>Genera exclusivamente migraciones de Compras sin iniciar workers.</summary>
public sealed class ComprasDesignTimeFactory : IDesignTimeDbContextFactory<ComprasDbContext>
{
    public ComprasDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<ComprasDbContext>()
            .UseNpgsql(DesignTimeConexion.Valor)
            .UseSnakeCaseNamingConvention().Options, new DesignTimeEmpresa());
}
