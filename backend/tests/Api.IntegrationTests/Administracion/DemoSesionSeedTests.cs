using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Millet.Api.Auth.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Millet.Api.Seed;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Blob;
using Millet.Compras.Infrastructure;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Blob;
using Millet.Tesoreria.Infrastructure.Persistence;
using Npgsql;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Usa el PostgreSQL desechable del gate, pero crea SU PROPIA base recién migrada.
/// No deja proveedores, artículos, usuarios ni periodos en los catálogos compartidos
/// con las otras suites. La base y los blobs se eliminan incluso si falla un assert.
/// </summary>
public sealed class DemoSesionSeedTests
{
    [Fact]
    public async Task DosArranques_NoDuplican_YConservanEstadosAdjuntosYUsuarios()
    {
        var conexionGate = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? throw new InvalidOperationException("Ejecutar tools/validate-integration-isolated.sh.");
        var nombre = "millet_demo_seed_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(conexionGate) { Database = "postgres", Pooling = false };
        var demo = new NpgsqlConnectionStringBuilder(conexionGate) { Database = nombre, Pooling = false };
        var blobs = Path.Combine(Path.GetTempPath(), nombre);
        var entornoDemo = new Dictionary<string, string?>
        {
            ["ConnectionStrings__Postgres"] = demo.ConnectionString,
            ["Seed__DemoSesion__Habilitado"] = "true",
            ["Seed__DatosDemo__Habilitado"] = "false",
        };
        var entornoOriginal = entornoDemo.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        await using var conexion = new NpgsqlConnection(admin.ConnectionString);
        await conexion.OpenAsync();
        // DDL exclusivo del fixture: el nombre lo genera el test, no viene del usuario.
        await using (var crear = new NpgsqlCommand($"CREATE DATABASE \"{nombre}\"", conexion)) await crear.ExecuteNonQueryAsync();
        try
        {
            // Program captura la conexión al registrar los contextos, antes del
            // override de ConfigureAppConfiguration. Aislar desde el arranque
            // también cubre servicios que capturen configuración y las variables
            // de TestAssemblyInit. Este assembly deshabilita el paralelismo.
            foreach (var (clave, valor) in entornoDemo)
                Environment.SetEnvironmentVariable(clave, valor);

            Snapshot primera;
            await using (var factory = CrearFactory(demo.ConnectionString, blobs))
            {
                primera = await VerificarAsync(factory);
                using var scope = factory.Services.CreateScope();
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
                Assert.False(await identidad.Usuarios.AnyAsync(u => u.Email == "compras-demo@example.invalid"));
                // Rack creado a mano con la misma clave, otro ID y otro nombre: se reutiliza.
                var almacen = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
                var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
                var ocMid = await compras.OrdenesCompra.SingleAsync(o => o.Id == DemoSesionSeedHostedService.Id("DEMO-OC-MID"));
                var subMid = await (from sub in almacen.SubAlmacenes
                                    join al in almacen.Almacenes on sub.AlmacenId equals al.Id
                                    where al.SucursalId == ocMid.SucursalDestinoId && sub.Clave == "INSUMOS"
                                    orderby al.Clave
                                    select sub).FirstAsync();
                var anterior = await almacen.Ubicaciones.SingleAsync(u => u.SubAlmacenId == subMid.Id && u.Clave == "R-01");
                await almacen.AsignacionesArticuloUbicacion.Where(a => a.UbicacionId == anterior.Id).ExecuteDeleteAsync();
                await almacen.SaldosInventario.Where(a => a.UbicacionId == anterior.Id).ExecuteDeleteAsync();
                almacen.Ubicaciones.Remove(anterior);
                await almacen.SaveChangesAsync();
                var manual = new Ubicacion(Guid.NewGuid(), subMid.Id, "R-01", "Rack creado a mano");
                almacen.Ubicaciones.Add(manual);
                await almacen.SaveChangesAsync();
                // Simula la referencia del seed anterior para verificar su reparación idempotente.
                var rqAnterior = await compras.Requisiciones.SingleAsync(r => r.Id == DemoSesionSeedHostedService.Id("DEMO-RQ-MID"));
                compras.Entry(rqAnterior).Property(r => r.RequisitanteId).CurrentValue = DemoSesionSeedHostedService.ActorId;
                await compras.SaveChangesAsync();
                await factory.Services.GetServices<IHostedService>().OfType<DemoSesionSeedHostedService>().Single().StartAsync(CancellationToken.None);
                almacen.ChangeTracker.Clear();
                Assert.Equal(manual.Id, (await almacen.Ubicaciones.SingleAsync(u => u.SubAlmacenId == subMid.Id && u.Clave == "R-01")).Id);
                Assert.Equal("Rack creado a mano", (await almacen.Ubicaciones.SingleAsync(u => u.Id == manual.Id)).Nombre);
                Assert.Equal(primera, await VerificarAsync(factory));
                // Simula exclusivamente en el fixture la identidad que el usuario ya
                // creó al iniciar sesión. El sembrador no inserta usuarios reales;
                // sus tres identidades P2 están etiquetadas como DEMO.
                identidad.Usuarios.Add(new Usuario(DemoSesionSeedHostedService.Id("DEMO-USUARIO-FIXTURE"),
                    Guid.NewGuid().ToString(), "compras-demo@example.invalid", "DEMO Compras", esCuentaTecnica: true));
                await identidad.SaveChangesAsync();
            }
            await using (var factory = CrearFactory(demo.ConnectionString, blobs))
            {
                Assert.Equal(primera, await VerificarAsync(factory));
                using var scope = factory.Services.CreateScope();
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
                var id = DemoSesionSeedHostedService.Id("DEMO-USUARIO-FIXTURE");
                var asignacion = await identidad.UsuarioEmpresaRoles.SingleAsync(a => a.UsuarioId == id);
                Assert.Equal(DemoSesionSeedHostedService.Id("DEMO-ROL-Compras"), asignacion.RolId);
                var alcance = await identidad.UsuarioSucursales.SingleAsync(a => a.UsuarioId == id && a.Estatus == EstatusCatalogo.Activo);
                var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
                Assert.Equal("MID", (await compartido.Sucursales.SingleAsync(s => s.Id == alcance.SucursalId)).Clave);
                Assert.Single(await identidad.Usuarios.Where(u => u.Email == "compras-demo@example.invalid").ToListAsync());
                // P6: verifica el recorrido HTTP con el perfil DEMO real del seed, sin bypass territorial.
                var usuario = await identidad.Usuarios.SingleAsync(u => u.Id == id);
                using var client = factory.CreateClientWithIdempotency();
                var loginRes = await client.PostAsJsonAsync("/api/dev/fake-login", new
                {
                    usuario.EntraOid, usuario.Email, usuario.Nombre,
                });
                loginRes.EnsureSuccessStatusCode();
                var login = (await loginRes.Content.ReadFromJsonAsync<LoginResponse>())!;
                Assert.DoesNotContain(PermisosCanonicos.ComprasOrdenesLeerTodasSucursales, login.Permisos);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
                var midId = DemoSesionSeedHostedService.Id("DEMO-OC-MID");
                var mtyId = DemoSesionSeedHostedService.Id("DEMO-OC-MTY");
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/compras/ordenes/{midId}")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/compras/ordenes/{mtyId}")).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/compras/requisiciones/{DemoSesionSeedHostedService.Id("DEMO-RQ-MID")}")).StatusCode);
                var listaOc = await client.GetStringAsync("/api/v1/compras/ordenes?limit=200");
                Assert.Contains(midId.ToString(), listaOc);
                Assert.DoesNotContain(mtyId.ToString(), listaOc);
                // Repetición después de operar: el seed no deshace la recepción parcial.
                var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
                var oc = await compras.OrdenesCompra.Include(o => o.Lineas).SingleAsync(o => o.ReferenciaProveedor == "DEMO-OC-MID");
                oc.RegistrarRecepcionLinea(oc.Lineas.Single().Id, 6m, DateTimeOffset.UtcNow);
                await compras.SaveChangesAsync();
                await factory.Services.GetServices<IHostedService>().OfType<DemoSesionSeedHostedService>().Single().StartAsync(CancellationToken.None);
                compras.ChangeTracker.Clear();
                var conservada = await compras.OrdenesCompra.Include(o => o.Lineas).SingleAsync(o => o.Id == oc.Id);
                Assert.Equal(6m, conservada.Lineas.Single().CantidadRecibida);
                Assert.Equal(1, await identidad.UsuarioEmpresaRoles.CountAsync(a => a.UsuarioId == id));
            }
        }
        finally
        {
            try
            {
                await using var borrar = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{nombre}\" WITH (FORCE)", conexion);
                await borrar.ExecuteNonQueryAsync();
                await using var existe = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @nombre)", conexion);
                existe.Parameters.AddWithValue("nombre", nombre);
                Assert.Equal(false, await existe.ExecuteScalarAsync());
            }
            finally
            {
                // Restaurar incluso si falla el host, un assert o DROP DATABASE.
                foreach (var (clave, valor) in entornoOriginal)
                    Environment.SetEnvironmentVariable(clave, valor);
                if (Directory.Exists(blobs)) Directory.Delete(blobs, recursive: true);
            }
        }
    }

    private static WebApplicationFactory<Program> CrearFactory(string conexion, string blobs) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = conexion,
                ["Seed:DemoSesion:Habilitado"] = "true",
                ["Seed:DatosDemo:Habilitado"] = "false",
                ["Seed:DemoSesion:Usuarios:0:Correo"] = "compras-demo@example.invalid",
                ["Seed:DemoSesion:Usuarios:0:Rol"] = "Compras",
                ["Seed:DemoSesion:Usuarios:0:Sucursales:0"] = "MID",
                ["Adjuntos:BlobStorage:ConnectionString"] = "",
                ["Compras:Oc:BlobStorage:ConnectionString"] = "",
            }));
            builder.ConfigureServices(services =>
            {
                // Los blobs de este fixture jamás apuntan a Azure ni a los de la demo.
                services.AddSingleton<IBlobStoragePort>(new BlobsFixture(blobs));
                services.AddSingleton<IAlmacenarBlobPort>(new BlobsFixture(blobs));
                services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(sp =>
                    new MigrarBaseDemo(sp.GetRequiredService<IServiceScopeFactory>(), conexion)));
            });
        });

    private static async Task<Snapshot> VerificarAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var maestros = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var contabilidad = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        var facturacion = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var tesoreria = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var empresa = Assert.Single(await maestros.Empresas.ToListAsync());
        Assert.Equal("MILLET INDUSTRIA DE VIDRIO (DEMO)", empresa.RazonSocial);
        Assert.Equal("42501", empresa.CodigoPostal);
        Assert.Equal("601", empresa.RegimenFiscal);
        Assert.False(await maestros.Articulos.AnyAsync(a => a.Clave.StartsWith("ART-TEST-")));
        Assert.False(await maestros.Proveedores.AnyAsync(p => p.RazonSocial.Contains("Test")));
        var revision = await maestros.Proveedores.SingleAsync(p => p.Clave == "DEMO-PROV-REV");
        var activo = await maestros.Proveedores.SingleAsync(p => p.Clave == "DEMO-PROV-ACT");
        Assert.Equal(EstatusCatalogo.EnRevision, revision.Estatus);
        Assert.Equal(EstatusCatalogo.Activo, activo.Estatus);
        Assert.NotNull(activo.Clabe);
        Assert.Equal(3, await maestros.Adjuntos.CountAsync(a => a.EntidadId == revision.Id));
        Assert.Equal(5, await maestros.Adjuntos.CountAsync(a => a.EntidadId == activo.Id));
        Assert.Equal(5, await maestros.AdjuntoTiposDocumento.CountAsync(t => t.TipoEntidad == "proveedor" && t.Activo && t.Obligatorio));
        var oc = await compras.OrdenesCompra.Include(o => o.Adjuntos).Include(o => o.Autorizaciones).Include(o => o.Lineas)
            .SingleAsync(o => o.ReferenciaProveedor == "DEMO-OC-MID");
        Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
        Assert.Equal(SubEstadoRecepcion.SinRecepcion, oc.SubEstadoRecepcion);
        Assert.Equal(2, oc.Adjuntos.Count);
        Assert.Equal(2, oc.Autorizaciones.Count);
        Assert.Equal(DemoSesionSeedHostedService.CapturistaComprasDemoId, oc.CompradorTitularId);
        Assert.Equal(DemoSesionSeedHostedService.JefeComprasDemoId,
            Assert.Single(oc.Autorizaciones, a => a.Nivel == Millet.Compras.Domain.NivelAutorizacion.Nivel1).UsuarioId);
        Assert.Equal(DemoSesionSeedHostedService.DireccionDemoId,
            Assert.Single(oc.Autorizaciones, a => a.Nivel == Millet.Compras.Domain.NivelAutorizacion.Nivel2).UsuarioId);
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        foreach (var (id, rol) in new[]
        {
            (DemoSesionSeedHostedService.CapturistaComprasDemoId, "Capturista Compras"),
            (DemoSesionSeedHostedService.JefeComprasDemoId, "Jefe Compras"),
            (DemoSesionSeedHostedService.DireccionDemoId, "Dirección"),
        })
        {
            var usuario = await identidad.Usuarios.SingleAsync(u => u.Id == id);
            Assert.True(usuario.Activo);
            Assert.True(usuario.EsCuentaTecnica);
            Assert.False(usuario.TieneOidPendiente);
            Assert.StartsWith("DEMO", usuario.Nombre);
            Assert.Equal(DemoSesionSeedHostedService.Id("DEMO-ROL-" + rol),
                (await identidad.UsuarioEmpresaRoles.SingleAsync(a => a.UsuarioId == id && a.EmpresaId == empresa.Id)).RolId);
            Assert.Equal(rol == "Capturista Compras" ? 1 : 3,
                await identidad.UsuarioSucursales.CountAsync(a => a.UsuarioId == id && a.EmpresaId == empresa.Id));
        }
        Assert.Equal(10m, Assert.Single(oc.Lineas).Cantidad);
        Assert.Equal(0m, Assert.Single(oc.Lineas).CantidadRecibida);
        foreach (var adjunto in oc.Adjuntos)
        {
            await using var archivo = await scope.ServiceProvider.GetRequiredService<IAlmacenarBlobPort>()
                .ObtenerStreamAsync(adjunto.BlobUrl, CancellationToken.None);
            Assert.True(archivo.Length > 500);
        }
        foreach (var adjunto in await maestros.Adjuntos.ToListAsync())
        {
            await using var archivo = await scope.ServiceProvider.GetRequiredService<IBlobStoragePort>()
                .ObtenerStreamAsync(adjunto.BlobRef, CancellationToken.None);
            Assert.Equal(adjunto.TamanoBytes, archivo.Length);
        }
        var periodos = await contabilidad.Periodos.Where(p => p.Anio == 2026).ToListAsync();
        Assert.Equal(13, periodos.Count);
        Assert.All(periodos.Where(p => p.Numero <= 9), p => Assert.Equal(EstadoPeriodo.Cerrado, p.Estado));
        Assert.Equal(EstadoPeriodo.Abierto, periodos.Single(p => p.Numero == 10).Estado);
        Assert.Equal(7, await contabilidad.Cuentas.CountAsync(c => c.Nombre.StartsWith("DEMO")));
        Assert.Equal(2, await tesoreria.CuentasBancarias.CountAsync(c => c.Banco.StartsWith("DEMO-")));
        Assert.Equal(2, await compras.OrdenesCompra.CountAsync());
        Assert.Equal(3, await compras.OutboxEntries.CountAsync());
        Assert.Equal(2, await maestros.Series.CountAsync(s => s.Prefijo.StartsWith("DEMO-")));
        var pedidos = await facturacion.PedidosFacturables.Where(p => p.NumeroPedido!.StartsWith("DEMO-")).ToListAsync();
        Assert.Equal(3, pedidos.Count);
        Assert.Equal(116m, pedidos.Single(p => p.NumeroPedido == "DEMO-PED-RANURA").Ranura);
        foreach (var pedido in pedidos) Assert.True(await maestros.Clientes.AnyAsync(c => c.Id == pedido.ClienteId));
        Assert.False((await maestros.Clientes.SingleAsync(c => c.Clave == "DEMO-CLI-INCOMPLETO")).DatosFiscalesCompletos);
        var rq = await compras.Requisiciones.Include(r => r.Lineas).SingleAsync(r => r.Descripcion == "DEMO-RQ-MID");
        Assert.Equal(10m, Assert.Single(rq.Lineas).CantidadDeCompra);
        Assert.Equal(oc.Id, rq.ComprometidaEnOcId);
        Assert.Equal(DemoSesionSeedHostedService.CapturistaComprasDemoId, rq.RequisitanteId);
        var usuarioPort = scope.ServiceProvider.GetRequiredService<Millet.Compras.Domain.Ports.Identidad.IUsuarioReadPort>();
        var nombres = await usuarioPort.ObtenerNombresAsync([rq.RequisitanteId], CancellationToken.None);
        Assert.Equal("DEMO Capturista Compras", nombres[rq.RequisitanteId]);
        var almacen = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        // Solo las OC de la sesión DEMO (la base es compartida con otras pruebas), con el mismo criterio del sembrado.
        var ocMidId = DemoSesionSeedHostedService.Id("DEMO-OC-MID");
        var ocMtyId = DemoSesionSeedHostedService.Id("DEMO-OC-MTY");
        foreach (var orden in await compras.OrdenesCompra.Include(o => o.Lineas)
                     .Where(o => o.Id == ocMidId || o.Id == ocMtyId
                         || (o.ReferenciaProveedor != null && o.ReferenciaProveedor.StartsWith("DEMO-")))
                     .ToListAsync())
        {
            var sub = await (from sa in almacen.SubAlmacenes
                             join al in almacen.Almacenes on sa.AlmacenId equals al.Id
                             where al.SucursalId == orden.SucursalDestinoId && sa.Clave == "INSUMOS"
                             orderby al.Clave
                             select sa).FirstAsync();
            var rack = await almacen.Ubicaciones.SingleAsync(u => u.SubAlmacenId == sub.Id && u.Clave == "R-01");
            Assert.False(rack.EsDefault);
            Assert.Equal(EstatusCatalogo.Activo, rack.Estatus);
            foreach (var linea in orden.Lineas)
            {
                var asignacion = Assert.Single(await almacen.AsignacionesArticuloUbicacion
                    .Where(a => a.UbicacionId == rack.Id && a.ArticuloId == linea.ArticuloId).ToListAsync());
                Assert.Equal(EstatusCatalogo.Activo, asignacion.Estatus);
                // Es el mismo guard que aplica el handler real de recepción contra OC.
                Assert.Equal(sub.Id, await UbicacionEntradaGuard.ValidarEntradaYDerivarSubAsync(
                    almacen, rack.Id, linea.ArticuloId, CancellationToken.None));
                Assert.NotNull(await almacen.SaldosInventario.FindAsync(rack.Id, linea.ArticuloId));
            }
        }
        Assert.Equal(2, await almacen.Ubicaciones.CountAsync(u => u.Clave == "R-01"));
        Assert.Equal(2, await almacen.AsignacionesArticuloUbicacion.CountAsync());
        var ocPort = scope.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Domain.Ports.Compras.IComprasOcReadPort>();
        var ocs = await compras.OrdenesCompra.ToListAsync();
        var folios = await ocPort.ObtenerFoliosAsync(ocs.Select(o => o.Id).Append(Guid.NewGuid()).ToArray(), CancellationToken.None);
        Assert.Equal(2, folios.Count);
        Assert.All(ocs, o => Assert.Equal(o.Folio.Valor, folios[o.Id]));
        return new Snapshot(await maestros.Proveedores.CountAsync(p => p.Clave.StartsWith("DEMO-")),
            await maestros.Articulos.CountAsync(a => a.Clave.StartsWith("DEMO-")), await maestros.Clientes.CountAsync(c => c.Clave.StartsWith("DEMO-")),
            await maestros.Adjuntos.CountAsync(), await compras.OrdenesCompra.CountAsync(), await compras.Requisiciones.CountAsync(),
            pedidos.Count, await tesoreria.CuentasBancarias.CountAsync(), periodos.Count, await contabilidad.Cuentas.CountAsync(),
            await contabilidad.Importaciones.CountAsync(), await compras.OutboxEntries.CountAsync());
    }

    private sealed record Snapshot(int Proveedores, int Articulos, int Clientes, int Adjuntos, int Ocs, int Rqs,
        int Pedidos, int Bancos, int Periodos, int Cuentas, int Lotes, int Outbox);

    private sealed class MigrarBaseDemo(IServiceScopeFactory scopes, string conexionEsperada) : IHostedService
    {
        public async Task StartAsync(CancellationToken ct)
        {
            using var scope = scopes.CreateScope();
            Assert.Equal(conexionEsperada,
                scope.ServiceProvider.GetRequiredService<IConfiguration>().GetConnectionString("Postgres"));
            Type[] contextos =
            [
                typeof(CompartidoDbContext), typeof(Millet.SharedKernel.Infrastructure.Persistence.CoreDbContext), typeof(IdentidadDbContext),
                typeof(ComprasDbContext), typeof(Millet.Integraciones.Aw.Infrastructure.Persistence.IntegracionesAwDbContext),
                typeof(Millet.Integraciones.Fiscal.Infrastructure.Persistence.IntegracionesFiscalDbContext),
                typeof(Millet.Almacen.Infrastructure.Persistence.AlmacenDbContext),
                typeof(Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext), typeof(FacturacionDbContext),
                typeof(Millet.CuentasPorCobrar.Infrastructure.Persistence.CuentasPorCobrarDbContext), typeof(TesoreriaDbContext),
                typeof(Millet.CentrosCosto.Infrastructure.Persistence.CentrosCostoDbContext), typeof(ContabilidadDbContext),
            ];
            var instancias = contextos.Select(tipo => (DbContext)scope.ServiceProvider.GetRequiredService(tipo)).ToArray();
            // Comprobar TODOS antes de que la primera migración escriba datos.
            Assert.All(instancias, db => Assert.Equal(conexionEsperada, db.Database.GetConnectionString()));
            foreach (var db in instancias)
                await db.Database.MigrateAsync(ct);
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class BlobsFixture(string root) : IBlobStoragePort, IAlmacenarBlobPort
    {
        public async Task SubirAsync(string clave, Stream contenido, string contentType, CancellationToken ct)
        {
            var path = Path.Combine(root, clave);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var archivo = File.Create(path);
            await contenido.CopyToAsync(archivo, ct);
        }
        public async Task<string> SubirAsync(Guid id, Stream contenido, string contentType, string nombre, CancellationToken ct)
        {
            var clave = id + ".pdf";
            await SubirAsync(clave, contenido, contentType, ct);
            return clave;
        }
        public Task<Stream> ObtenerStreamAsync(string clave, CancellationToken ct) => Task.FromResult<Stream>(File.OpenRead(Path.Combine(root, clave)));
        public Task EliminarAsync(string clave, CancellationToken ct) { File.Delete(Path.Combine(root, clave)); return Task.CompletedTask; }
    }
}
