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
                .OrderBy(x => x.Clave).ToArrayAsync(token);
            if (areas.Length == 0) return;
            var existentes = await db.DepartamentoCentrosCosto.ToListAsync(token);
            var nombres = await organizacion.Departamentos.AsNoTracking().Where(d => departamentos.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Nombre, token);
            for (var i = 0; i < asignaciones.Count; i++)
            {
                var a = asignaciones[i];
                var existente = existentes.SingleOrDefault(x => x.EmpresaId == a.EmpresaId && x.SucursalId == a.SucursalId && x.DepartamentoId == a.DepartamentoId);
                const string marcaAnterior = "DEMO · por validar con Laura (V49) · equivalencia ficticia, no aprobada";
                if (existente is not null && existente.Observaciones != marcaAnterior) continue;
                var matches = areas.Where(area => CorrespondeAlDepartamento(nombres[a.DepartamentoId], area.Nombre)).ToArray();
                // Nunca elegir un área por posición: sin coincidencia única queda pendiente la equivalencia.
                if (matches.Length != 1)
                {
                    if (existente is not null) db.DepartamentoCentrosCosto.Remove(existente);
                    continue;
                }
                var motivo = "DEMO · coincidencia con el nombre del departamento; por validar con Contabilidad";
                if (existente is not null) existente.Cambiar(matches[0].Id, motivo);
                else db.DepartamentoCentrosCosto.Add(new DepartamentoCentroCosto(Guid.CreateVersion7(), a.EmpresaId,
                    a.SucursalId, a.DepartamentoId, matches[0].Id, motivo));
            }
            await db.SaveChangesAsync(token);
        }, cancellationToken);
    }
    public static bool CorrespondeAlDepartamento(string departamento, string area)
    {
        static string Normalizar(string texto) => new string(texto.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant().Trim();
        var nombre = Normalizar(departamento);
        var destino = Normalizar(area);
        return nombre == destino || destino.StartsWith(nombre + " ", StringComparison.Ordinal);
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
