using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Millet.Api.Web;

// AF-04: inspección de TODOS los modelos sin iniciar el host ni resolver servicios externos.
public sealed class AF04CoreDbContextFactory : IDesignTimeDbContextFactory<Millet.SharedKernel.Infrastructure.Persistence.CoreDbContext>
{
    public Millet.SharedKernel.Infrastructure.Persistence.CoreDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<Millet.SharedKernel.Infrastructure.Persistence.CoreDbContext>()
            .UseNpgsql(DesignTimeConexion.Valor).UseSnakeCaseNamingConvention().Options, new DesignTimeEmpresa());
}

public sealed class AF04IntegracionesAwDbContextFactory : IDesignTimeDbContextFactory<Millet.Integraciones.Aw.Infrastructure.Persistence.IntegracionesAwDbContext>
{
    public Millet.Integraciones.Aw.Infrastructure.Persistence.IntegracionesAwDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<Millet.Integraciones.Aw.Infrastructure.Persistence.IntegracionesAwDbContext>()
            .UseNpgsql(DesignTimeConexion.Valor).UseSnakeCaseNamingConvention().Options, new DesignTimeEmpresa());
}

public sealed class AF04IntegracionesFiscalDbContextFactory : IDesignTimeDbContextFactory<Millet.Integraciones.Fiscal.Infrastructure.Persistence.IntegracionesFiscalDbContext>
{
    public Millet.Integraciones.Fiscal.Infrastructure.Persistence.IntegracionesFiscalDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<Millet.Integraciones.Fiscal.Infrastructure.Persistence.IntegracionesFiscalDbContext>()
            .UseNpgsql(DesignTimeConexion.Valor).UseSnakeCaseNamingConvention().Options, new DesignTimeEmpresa());
}

public sealed class AF04CuentasPorPagarDbContextFactory : IDesignTimeDbContextFactory<Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext>
{
    public Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext>()
            .UseNpgsql(DesignTimeConexion.Valor).UseSnakeCaseNamingConvention().Options, new DesignTimeEmpresa(), null!, null!);
}

public sealed class AF04CuentasPorCobrarDbContextFactory : IDesignTimeDbContextFactory<Millet.CuentasPorCobrar.Infrastructure.Persistence.CuentasPorCobrarDbContext>
{
    public Millet.CuentasPorCobrar.Infrastructure.Persistence.CuentasPorCobrarDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<Millet.CuentasPorCobrar.Infrastructure.Persistence.CuentasPorCobrarDbContext>()
            .UseNpgsql(DesignTimeConexion.Valor).UseSnakeCaseNamingConvention().Options, new DesignTimeEmpresa());
}

public sealed class AF04TesoreriaDbContextFactory : IDesignTimeDbContextFactory<Millet.Tesoreria.Infrastructure.Persistence.TesoreriaDbContext>
{
    public Millet.Tesoreria.Infrastructure.Persistence.TesoreriaDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<Millet.Tesoreria.Infrastructure.Persistence.TesoreriaDbContext>()
            .UseNpgsql(DesignTimeConexion.Valor).UseSnakeCaseNamingConvention().Options, new DesignTimeEmpresa());
}

