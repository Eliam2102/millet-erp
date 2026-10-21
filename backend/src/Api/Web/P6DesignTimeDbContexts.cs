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

public sealed class ComercioExteriorDesignTimeFactory : IDesignTimeDbContextFactory<Millet.ComercioExterior.Infrastructure.Persistence.ComercioExteriorDbContext>
{
    public Millet.ComercioExterior.Infrastructure.Persistence.ComercioExteriorDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<Millet.ComercioExterior.Infrastructure.Persistence.ComercioExteriorDbContext>()
            .UseNpgsql(DesignTimeConexion.Valor).UseSnakeCaseNamingConvention().Options, new DesignTimeEmpresa());
}

public sealed class FacturacionDesignTimeFactory : IDesignTimeDbContextFactory<Millet.Facturacion.Infrastructure.Persistence.FacturacionDbContext>
{
    public Millet.Facturacion.Infrastructure.Persistence.FacturacionDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<Millet.Facturacion.Infrastructure.Persistence.FacturacionDbContext>()
            .UseNpgsql(DesignTimeConexion.Valor).UseSnakeCaseNamingConvention().Options, new DesignTimeEmpresa());
}

public sealed class AlmacenDesignTimeFactory : IDesignTimeDbContextFactory<Millet.Almacen.Infrastructure.Persistence.AlmacenDbContext>
{
    public Millet.Almacen.Infrastructure.Persistence.AlmacenDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<Millet.Almacen.Infrastructure.Persistence.AlmacenDbContext>()
            .UseNpgsql(DesignTimeConexion.Valor).UseSnakeCaseNamingConvention().Options, new DesignTimeEmpresa());
}
