using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Seed;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Persistence;
namespace Millet.CentrosCosto.Infrastructure.Seed;

/// <summary>Semilla ficticia opt-in; nunca reemplaza equivalencias mantenidas ni catálogos M1.</summary>
public sealed class ADM08EquivalenciasDemoHostedService(IServiceScopeFactory scopes,
    IHostEnvironment environment, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (environment.IsProduction() || !(configuration.GetValue<bool>("Seed:DatosDemo:Habilitado") ||
            configuration.GetValue<bool>("Seed:DemoSesion:Habilitado"))) return;
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(ADM08EquivalenciasDemoHostedService));
        var db = sp.GetRequiredService<CentrosCostoDbContext>();
        var organizacion = sp.GetRequiredService<CompartidoDbContext>();
        await PostgresAdvisoryLock.ExecuteAsync(db, 6_672_000_008, async token =>
        {
            var departamentos = CatalogosTestSeedHostedService.TestDepartamentos
                .Concat(CatalogosTestSeedHostedService.DemoDepartamentos).Select(x => x.Id).ToArray();
            var sucursales = CatalogosTestSeedHostedService.TestSucursales.Select(x => x.Id).ToArray();
            var asignaciones = await organizacion.SucursalDepartamentos.AsNoTracking()
                .Where(x => departamentos.Contains(x.DepartamentoId) && sucursales.Contains(x.SucursalId) && x.Estatus == EstatusCatalogo.Activo)
                .OrderBy(x => x.SucursalId).ThenBy(x => x.DepartamentoId).ToListAsync(token);
            var areas = await db.Dim2s.AsNoTracking().Where(x => x.Estatus == EstatusCatalogo.Activo &&
                db.Dim1s.Any(p => p.Id == x.Dim1Id && p.Estatus == EstatusCatalogo.Activo))
                .OrderBy(x => x.Clave).Select(x => x.Id).ToArrayAsync(token);
            if (areas.Length == 0) return;
            var existentes = await db.DepartamentoCentrosCosto.AsNoTracking().ToListAsync(token);
            for (var i = 0; i < asignaciones.Count; i++)
            {
                var a = asignaciones[i];
                if (existentes.Any(x => x.EmpresaId == a.EmpresaId && x.SucursalId == a.SucursalId && x.DepartamentoId == a.DepartamentoId)) continue;
                db.DepartamentoCentrosCosto.Add(new DepartamentoCentroCosto(Guid.CreateVersion7(), a.EmpresaId,
                    a.SucursalId, a.DepartamentoId, areas[i % areas.Length], "DEMO · por validar con Laura (V49) · equivalencia ficticia, no aprobada"));
            }
            await db.SaveChangesAsync(token);
        }, cancellationToken);
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
