using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Identidad.UnitTests.Infrastructure;

public sealed class RolesActivosDafTests
{
    [Fact]
    public async Task Seed_repetido_conserva_registro_y_retira_autorizaciones_historicas()
    {
        using var db = new IdentidadDbContext(new DbContextOptionsBuilder<IdentidadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
        await db.Database.EnsureCreatedAsync();
        var contador = new Rol(Guid.NewGuid(), "contador-historico", "Contador General");
        db.Roles.Add(contador);
        foreach (var permiso in PermisosCanonicos.Todos.Where(p => p.Codigo is
            PermisosCanonicos.FacturacionActivosAutorizar or PermisosCanonicos.ActivosFijosBajasAutorizar))
            db.RolPermisos.Add(new RolPermiso(Guid.NewGuid(), contador.Id, permiso.Id));
        await db.SaveChangesAsync();
        var servicio = new BootstrapSuperAdminHostedService(null!, Options.Create(new BootstrapSuperAdminOptions()),
            NullLogger<BootstrapSuperAdminHostedService>.Instance);
        var seed = typeof(BootstrapSuperAdminHostedService).GetMethod("EnsureRolesMvpAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)seed.Invoke(servicio, [db, CancellationToken.None])!;
        var total = await db.RolPermisos.CountAsync();
        await (Task)seed.Invoke(servicio, [db, CancellationToken.None])!;
        Assert.Equal(total, await db.RolPermisos.CountAsync());
        var daf = await db.Roles.SingleAsync(r => r.Codigo == "director-administracion-finanzas");
        var autorizaciones = PermisosCanonicos.Todos.Where(p => p.Codigo is
            PermisosCanonicos.FacturacionActivosAutorizar or PermisosCanonicos.ActivosFijosBajasAutorizar).Select(p => p.Id).ToArray();
        Assert.Equal(2, await db.RolPermisos.CountAsync(r => r.RolId == daf.Id && autorizaciones.Contains(r.PermisoId)));
        foreach (var rol in await db.Roles.Where(r => r.Nombre == "Contador General").ToListAsync())
        {
            Assert.False(await db.RolPermisos.AnyAsync(r => r.RolId == rol.Id && autorizaciones.Contains(r.PermisoId)));
            var registrar = PermisosCanonicos.Todos.Single(p => p.Codigo == PermisosCanonicos.ActivosFijosBajasRegistrar).Id;
            Assert.True(await db.RolPermisos.AnyAsync(r => r.RolId == rol.Id && r.PermisoId == registrar));
        }
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
