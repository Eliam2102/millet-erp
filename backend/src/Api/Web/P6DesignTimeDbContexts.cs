using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.Web;

/// <summary>Generación de migraciones sin arrancar workers ni cargar credenciales del API.</summary>
public sealed class IdentidadDesignTimeFactory : IDesignTimeDbContextFactory<IdentidadDbContext>
{
    public IdentidadDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<IdentidadDbContext>()
            .UseNpgsql(DesignTimeConexion.Valor)
            .UseSnakeCaseNamingConvention().Options, new DesignTimeEmpresa());
}
public sealed class CompartidoDesignTimeFactory : IDesignTimeDbContextFactory<CompartidoDbContext>
{
    public CompartidoDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseNpgsql(DesignTimeConexion.Valor)
            .UseSnakeCaseNamingConvention().Options, new DesignTimeEmpresa());
}
/// <summary>Usa la conexión del entorno (scripts y despliegue); sin ella, una ficticia que solo sirve para generar migraciones.</summary>
internal static class DesignTimeConexion
{
    public static string Valor =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
        ?? "Host=localhost;Database=millet_design_time;Username=design_time";
}
internal sealed class DesignTimeEmpresa : ICurrentEmpresaContext
{
    public Guid? Current => null;
    public bool IsBypassed => true;
    public IDisposable Bypass() => new Noop();
    private sealed class Noop : IDisposable { public void Dispose() { } }
}
