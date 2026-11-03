using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Identidad.UnitTests.Infrastructure;

/// <summary>REQ-PERM (86e3xd4uz): los 6 roles del Portal de Requisiciones se siembran sin duplicarse.</summary>
public sealed class RolesPortalRequisicionesTests
{
    private static readonly string[] Codigos =
    [
        "requisitante", "requisitante-a-nombre-de-otros", "autorizador-requisiciones",
        "almacen-salida-contra-requisicion", "almacen-vale-urgente", "cancelacion-requisicion-autorizada",
    ];

    [Fact]
    public async Task Seed_dos_veces_deja_seis_roles_sin_duplicados_ni_permisos_repetidos()
    {
        using var db = await SembrarAsync(veces: 2);

        var roles = await db.Roles.Where(r => Codigos.Contains(r.Codigo)).ToListAsync();
        Assert.Equal(6, roles.Count);
        Assert.All(roles, r => Assert.True(r.EsDelSistema));
        var permisos = await db.RolPermisos.ToListAsync();
        Assert.Equal(permisos.Count, permisos.Select(p => (p.RolId, p.PermisoId)).Distinct().Count());
    }

    [Fact]
    public async Task Requisitante_no_autoriza_y_vale_urgente_no_cancela()
    {
        using var db = await SembrarAsync(veces: 1);

        var requisitante = await PermisosAsync(db, "requisitante");
        Assert.DoesNotContain(PermisosCanonicos.ComprasRequisicionesAutorizarNivel1, requisitante);
        Assert.DoesNotContain(PermisosCanonicos.ComprasRequisicionesAutorizarNivel2, requisitante);
        Assert.DoesNotContain(PermisosCanonicos.ComprasRequisicionesSeleccionarRequisitante, requisitante);

        var vale = await PermisosAsync(db, "almacen-vale-urgente");
        Assert.Contains(PermisosCanonicos.AlmacenSalidasPorVale, vale);
        Assert.DoesNotContain(PermisosCanonicos.ComprasRequisicionesCancelar, vale);

        var autorizador = await PermisosAsync(db, "autorizador-requisiciones");
        Assert.Contains(PermisosCanonicos.ComprasRequisicionesAutorizarNivel1, autorizador);
        Assert.Contains(PermisosCanonicos.ComprasRequisicionesAutorizarNivel2, autorizador);
        Assert.Contains(PermisosCanonicos.ComprasRequisicionesCancelar, await PermisosAsync(db, "cancelacion-requisicion-autorizada"));
        Assert.Contains(PermisosCanonicos.ComprasRequisicionesSeleccionarRequisitante, await PermisosAsync(db, "requisitante-a-nombre-de-otros"));
        Assert.Contains(PermisosCanonicos.AlmacenSalidasRegistrar, await PermisosAsync(db, "almacen-salida-contra-requisicion"));
    }

    private static async Task<IdentidadDbContext> SembrarAsync(int veces)
    {
        var db = new IdentidadDbContext(new DbContextOptionsBuilder<IdentidadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
        await db.Database.EnsureCreatedAsync();
        var servicio = new BootstrapSuperAdminHostedService(null!, Options.Create(new BootstrapSuperAdminOptions()),
            NullLogger<BootstrapSuperAdminHostedService>.Instance);
        var seed = typeof(BootstrapSuperAdminHostedService).GetMethod("EnsureRolesMvpAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var i = 0; i < veces; i++) await (Task)seed.Invoke(servicio, [db, CancellationToken.None])!;
        return db;
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
