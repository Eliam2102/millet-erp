using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Api;
using Millet.Api.Seed;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Npgsql;

namespace Millet.Api.IntegrationTests.Administracion;

public sealed partial class DemoSesionSeedTests
{
    [Theory]
    [InlineData(false, "Jefe Compras y CxP")]
    [InlineData(true, "Jefe Compras y CxP")]
    [InlineData(false, "Dirección y Tesorería")]
    [InlineData(true, "Dirección y Tesorería")]
    public async Task Asignaciones_BanderaControlaReemplazo_YDosArranquesNoDuplican(bool reemplazar, string rol)
    {
        await ConBaseEntraAsync(async services =>
        {
            var usuarioId = Guid.NewGuid();
            var oid = Guid.NewGuid().ToString();
            var correo = "jefe-pagos@example.invalid";
            var rolAnterior = DemoSesionSeedHostedService.Id("DEMO-ROL-Contabilidad");
            var otroRolAnterior = DemoSesionSeedHostedService.Id("DEMO-ROL-Facturación");
            Guid mid, mty;
            using (var scope = services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
                var sedes = await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Sucursales.ToDictionaryAsync(s => s.Clave);
                mid = sedes["MID"].Id;
                mty = sedes["MTY"].Id;
                db.Usuarios.Add(new Usuario(usuarioId, oid, correo, "DEMO Jefe y pagos", esCuentaTecnica: true));
                db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.NewGuid(), usuarioId, DemoSesionSeedHostedService.EmpresaId, rolAnterior));
                db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.NewGuid(), usuarioId, DemoSesionSeedHostedService.EmpresaId, otroRolAnterior));
                db.UsuarioSucursales.Add(new UsuarioSucursal(Guid.NewGuid(), usuarioId, mty, DemoSesionSeedHostedService.EmpresaId));
                db.UsuarioSucursales.Add(new UsuarioSucursal(Guid.NewGuid(), usuarioId, mid, DemoSesionSeedHostedService.EmpresaId, EstatusCatalogo.Inactivo));
                await db.SaveChangesAsync();
            }

            var config = ConfiguracionAsignaciones(correo.ToUpperInvariant(), reemplazar, rol: rol);
            for (var arranque = 0; arranque < 2; arranque++)
            {
                await ArrancarSemillaAsync(services, config);
                using var scope = services.CreateScope();
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
                var roles = await db.UsuarioEmpresaRoles.Where(a => a.UsuarioId == usuarioId && a.EmpresaId == DemoSesionSeedHostedService.EmpresaId)
                    .Select(a => a.RolId).ToListAsync();
                var esperados = reemplazar
                    ? new[] { DemoSesionSeedHostedService.Id("DEMO-ROL-" + rol) }
                    : new[] { rolAnterior, otroRolAnterior };
                Assert.Equal(esperados.Order(), roles.Order());
                if (reemplazar) Assert.Single(roles);
                var sucursales = await db.UsuarioSucursales.Where(a => a.UsuarioId == usuarioId).ToListAsync();
                Assert.Equal(2, sucursales.Count);
                Assert.Equal(reemplazar ? mid : mty, Assert.Single(sucursales, a => a.Estatus == EstatusCatalogo.Activo).SucursalId);
            }
        });
    }

    [Fact]
    public async Task Asignaciones_RechazaAdministradorInicial_AntesDeCambiarSusAsignaciones()
    {
        await ConBaseEntraAsync(async services =>
        {
            var oid = Guid.NewGuid().ToString();
            var usuarioId = Guid.NewGuid();
            var correo = "admin-inicial@example.invalid";
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
                db.Usuarios.Add(new Usuario(usuarioId, oid, correo, "DEMO Administrador inicial", esCuentaTecnica: true));
                await db.SaveChangesAsync();
            }
            var config = ConfiguracionAsignaciones(correo, true, oid.ToUpperInvariant());
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ArrancarSemillaAsync(services, config));
            Assert.Contains("el administrador queda fuera de las cuentas del ensayo", error.Message);
            using var verificacion = services.CreateScope();
            using var bypass = verificacion.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var identidad = verificacion.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            Assert.False(await identidad.UsuarioEmpresaRoles.AnyAsync(a => a.UsuarioId == usuarioId));
            Assert.False(await identidad.UsuarioSucursales.AnyAsync(a => a.UsuarioId == usuarioId));
        });
    }

    [Fact]
    public async Task Asignaciones_RechazaSuperAdmin_SinCambiarAsignaciones_YVersionEsAnonima()
    {
        await ConBaseEntraAsync(async services =>
        {
            var usuarioId = Guid.NewGuid();
            var correo = "admin-adicional@example.invalid";
            Guid superAdminId;
            using (var scope = services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
                var rol = await db.Roles.SingleOrDefaultAsync(r => r.Codigo == "super-admin");
                if (rol is null)
                {
                    rol = new Rol(Guid.NewGuid(), "super-admin", "Super administrador", esDelSistema: true);
                    db.Roles.Add(rol);
                }
                superAdminId = rol.Id;
                db.Usuarios.Add(new Usuario(usuarioId, Guid.NewGuid().ToString(), correo, "DEMO Administrador adicional", esCuentaTecnica: true));
                db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.NewGuid(), usuarioId, DemoSesionSeedHostedService.EmpresaId, superAdminId));
                await db.SaveChangesAsync();
            }
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ArrancarSemillaAsync(services, ConfiguracionAsignaciones(correo, true)));
            Assert.Contains("el administrador queda fuera de las cuentas del ensayo", error.Message);
            using var verificacion = services.CreateScope();
            using var verificarBypass = verificacion.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var identidad = verificacion.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            var roles = await identidad.UsuarioEmpresaRoles.Where(a => a.UsuarioId == usuarioId).ToListAsync();
            Assert.Equal(superAdminId, Assert.Single(roles).RolId);
            Assert.False(await identidad.UsuarioSucursales.AnyAsync(a => a.UsuarioId == usuarioId));
        }, verificarVersion: true);
    }

    private static IConfiguration ConfiguracionAsignaciones(string correo, bool reemplazar, string adminOid = "", string rol = "Jefe Compras y CxP") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:DemoSesion:Habilitado"] = "true",
            ["Seed:DemoSesion:ReemplazarAsignaciones"] = reemplazar.ToString(),
            ["Seed:DemoSesion:Usuarios:0:Correo"] = correo,
            ["Seed:DemoSesion:Usuarios:0:Rol"] = rol,
            ["Seed:DemoSesion:Usuarios:0:Sucursales:0"] = "MID",
            ["Auth:InitialAdminEntraOid"] = adminOid,
        }).Build();

    private static Task ArrancarSemillaAsync(IServiceProvider services, IConfiguration config) =>
        new DemoSesionSeedHostedService(services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IHostEnvironment>(), config, NullLogger<DemoSesionSeedHostedService>.Instance)
            .StartAsync(CancellationToken.None);

    private static async Task ConBaseEntraAsync(Func<IServiceProvider, Task> verificar, bool verificarVersion = false)
    {
        var conexionGate = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? throw new InvalidOperationException("Ejecutar tools/validate-integration-isolated.sh.");
        var nombre = "millet_demo_roles_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(conexionGate) { Database = "postgres", Pooling = false };
        var demo = new NpgsqlConnectionStringBuilder(conexionGate) { Database = nombre, Pooling = false };
        var blobs = Path.Combine(Path.GetTempPath(), nombre);
        // El host usa Entra; se insertan identidades de fixture directamente y no se inicia sesión.
        var entorno = new Dictionary<string, string?>
        {
            ["ConnectionStrings__Postgres"] = demo.ConnectionString,
            ["Auth__Mode"] = "EntraId",
            ["Auth__InitialAdminEntraOid"] = "",
            ["Seed__DemoSesion__Habilitado"] = "true",
            ["Seed__DemoSesion__ReemplazarAsignaciones"] = "false",
            ["Seed__DatosDemo__Habilitado"] = "false",
        };
        var original = entorno.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        await using var conexion = new NpgsqlConnection(admin.ConnectionString);
        await conexion.OpenAsync();
        await using (var crear = new NpgsqlCommand($"CREATE DATABASE \"{nombre}\"", conexion)) await crear.ExecuteNonQueryAsync();
        try
        {
            foreach (var (clave, valor) in entorno) Environment.SetEnvironmentVariable(clave, valor);
            await using var factory = CrearFactory(demo.ConnectionString, blobs);
            if (verificarVersion)
            {
                using var client = factory.CreateClient();
                var version = await client.GetFromJsonAsync<VersionInfo>("/api/version");
                Assert.NotNull(version);
                Assert.Equal("Development", version.Ambiente);
                Assert.NotEmpty(version.Commit!);
                Assert.True(DateTimeOffset.TryParse(version.FechaCompilacion, out _));
            }
            await verificar(factory.Services);
        }
        finally
        {
            try
            {
                await using var borrar = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{nombre}\" WITH (FORCE)", conexion);
                await borrar.ExecuteNonQueryAsync();
            }
            finally
            {
                foreach (var (clave, valor) in original) Environment.SetEnvironmentVariable(clave, valor);
                if (Directory.Exists(blobs)) Directory.Delete(blobs, recursive: true);
            }
        }
    }
}
