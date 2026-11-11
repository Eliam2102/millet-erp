using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Identidad.UnitTests.Infrastructure;

/// <summary>M-327 (Millet, 10-nov-2026): la reversa de un anticipo comprobado la autoriza el Contador General, no la Dirección.</summary>
public sealed class M327PermisosReversaTests
{
    [Fact]
    public async Task Contador_general_autoriza_la_reversa_y_la_direccion_no()
    {
        using var db = new IdentidadDbContext(new DbContextOptionsBuilder<IdentidadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
        await db.Database.EnsureCreatedAsync();
        var servicio = new BootstrapSuperAdminHostedService(null!, Options.Create(new BootstrapSuperAdminOptions()),
            NullLogger<BootstrapSuperAdminHostedService>.Instance);
        var seed = typeof(BootstrapSuperAdminHostedService).GetMethod("EnsureRolesMvpAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)seed.Invoke(servicio, [db, CancellationToken.None])!;

        var contador = await PermisosAsync(db, "contador-general");
        Assert.Contains(PermisosCanonicos.ComercioExteriorAnticiposRevertirComprobacionAutorizar, contador);
        Assert.Contains(PermisosCanonicos.ComercioExteriorExpedientesLeer, contador);
        Assert.DoesNotContain(PermisosCanonicos.ComercioExteriorAnticiposRevertirComprobacionSolicitar, contador);
        Assert.DoesNotContain(PermisosCanonicos.ComercioExteriorAnticiposRevertirComprobacionAutorizar, await PermisosAsync(db, "director-administracion-finanzas"));
        Assert.DoesNotContain(PermisosCanonicos.ComercioExteriorAnticiposRevertirComprobacionAutorizar, await PermisosAsync(db, "tesoreria"));
        Assert.Contains(PermisosCanonicos.Todos, p => p.Codigo == PermisosCanonicos.ComercioExteriorAnticiposRevertirComprobacionSolicitar);
    }

    private static async Task<HashSet<string>> PermisosAsync(IdentidadDbContext db, string codigoRol)
    {
        var rolId = (await db.Roles.SingleAsync(r => r.Codigo == codigoRol)).Id;
        var ids = (await db.RolPermisos.Where(rp => rp.RolId == rolId).ToListAsync()).Select(rp => rp.PermisoId).ToHashSet();
        return PermisosCanonicos.Todos.Where(p => ids.Contains(p.Id)).Select(p => p.Codigo).ToHashSet();
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
