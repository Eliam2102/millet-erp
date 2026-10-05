using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Application.Cambios;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Application.Workers;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Cambios;
using Millet.Integraciones.Aw.Infrastructure.Clientes;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Productos;
using Millet.SharedKernel.Application;
using Xunit.Abstractions;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>
/// De extremo a extremo (sandbox): cambio en A+W → CDC real → <see cref="AwCambiosAplicador"/> → sincronizadores reales → PostgreSQL.
/// OPT-IN (<c>AW_SANDBOX_CONN</c>). El job de captura de CDC sondea cada ~5 s: se espera a que el LSN se estabilice.
/// </summary>
[Trait("Category", "AwSandbox")]
[Collection("AwClientesSync")]
public class AwCambiosAplicadorSandboxTests(WebApplicationFactory<Program> factory, ITestOutputHelper salida)
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private static readonly string? ConnEnv = Environment.GetEnvironmentVariable("AW_SANDBOX_CONN") is { Length: > 0 } v ? v : null;

    private sealed class Fabrica(string cadena) : IIntegracionSqlConnectionFactory
    {
        public System.Data.Common.DbConnection CreateConnection() => new SqlConnection(cadena);
    }

    private static string Cadena() => new SqlConnectionStringBuilder(ConnEnv!)
    {
        Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = true, // solo sandbox autofirmado
        ApplicationIntent = ApplicationIntent.ReadOnly,
    }.ConnectionString;

    private static readonly AwPedidosOptions Plomeria = new() { SqlConnectTimeoutSeconds = 5, SqlQueryTimeoutSeconds = 15 };

    public async Task InitializeAsync()
    {
        if (ConnEnv is null) return;
        await Script("reseed");
        await LimpiarAsync();
    }

    public async Task DisposeAsync()
    {
        if (ConnEnv is null) return;
        try { await Script("reseed"); } finally { await LimpiarAsync(); }
    }

    private static async Task Script(params string[] args)
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "tools", "aw-sandbox", "aw-sandbox.sh"))) raiz = raiz.Parent;
        var psi = new ProcessStartInfo("bash") { WorkingDirectory = raiz!.FullName, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(Path.Combine(raiz.FullName, "tools", "aw-sandbox", "aw-sandbox.sh"));
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        _ = p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token);
        if (p.ExitCode != 0) throw new InvalidOperationException($"aw-sandbox.sh {string.Join(' ', args)} falló: {await err}");
    }

    private static async Task EsperarCapturaAsync(AwCambiosCdcOrigen o)
    {
        var previo = await o.ObtenerLsnActualAsync(default);
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(6));
            var actual = await o.ObtenerLsnActualAsync(default);
            if (actual == previo) return;
            previo = actual;
        }
        throw new TimeoutException("El LSN de CDC no se estabilizó.");
    }

    private async Task LimpiarAsync()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var refs = Enumerable.Range(1, 2000).Select(i => i.ToString()).ToList();
        var aw = scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>();
        await aw.CdcWatermarks.ExecuteDeleteAsync();
        await aw.ClientesEjecuciones.Where(e => e.Estado == AwClientesEjecucionEstado.Pendiente || e.Estado == AwClientesEjecucionEstado.EnCurso)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Estado, AwClientesEjecucionEstado.Cancelada));
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        await db.Set<ClienteSincronizacionAw>().Where(r => refs.Contains(r.ReferenciaExterna)).ExecuteDeleteAsync();
        await db.Clientes.IgnoreQueryFilters().Where(c => c.Origen == OrigenMaster.Aw && c.ReferenciaExterna != null && refs.Contains(c.ReferenciaExterna)).ExecuteDeleteAsync();
        var ids = await db.ProductosAw.IgnoreQueryFilters().Where(p => refs.Contains(p.ReferenciaExterna)).Select(p => p.Id).ToListAsync();
        await db.ProductosAwVariantes.Where(v => ids.Contains(v.ProductoAwId)).ExecuteDeleteAsync();
        await db.ProductosSincronizacionAw.Where(r => ids.Contains(r.ProductoAwId)).ExecuteDeleteAsync();
        await db.ProductosAw.IgnoreQueryFilters().Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync();
    }

    /// <summary>Un ciclo del aplicador con CDC real y los sincronizadores reales (origen SQL del sandbox).</summary>
    private async Task<AwCambiosCicloResumen> Ciclo(AwEntidadCambio entidad, AwCambiosCdcOrigen cdc)
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = sp.GetRequiredService<IntegracionesAwDbContext>();
        var cdb = sp.GetRequiredService<CompartidoDbContext>();
        var co = new AwClientesOptions
        {
            Origen = AwClientesOrigenTipo.Sql, LecturaHabilitada = true, AplicacionHabilitada = true, TamanoLote = 50,
            SqlConnectTimeoutSeconds = 5, SqlQueryTimeoutSeconds = 15,
            MapeoMoneda = new(StringComparer.OrdinalIgnoreCase) { ["PESOSMX"] = "MXN", ["USD"] = "USD", ["Euro"] = "EUR", ["<indf>"] = "MXN" },
        };
        var po = new AwProductosOptions
        {
            OrigenHabilitado = true, Origen = AwProductosOrigenTipo.Sql, TamanoLote = 50, SqlConnectTimeoutSeconds = 5, SqlQueryTimeoutSeconds = 15,
        };
        var cliOrigen = new AwClientesSqlOrigen(new Fabrica(Cadena()), Options.Create(co), NullLogger<AwClientesSqlOrigen>.Instance);
        var prodOrigen = new AwProductosSqlOrigen(new Fabrica(Cadena()), Options.Create(po), NullLogger<AwProductosSqlOrigen>.Instance);
        var cliSp = new ServiceCollection().AddSingleton<IAwClientesOrigen>(cliOrigen).BuildServiceProvider();
        var prodSp = new ServiceCollection().AddSingleton<IAwProductosOrigen>(prodOrigen).BuildServiceProvider();
        var cliSync = new AwClientesSincronizador(db, cdb, sp.GetRequiredService<AplicarClienteAwService>(), cliSp, Options.Create(co),
            TimeProvider.System, NullLogger<AwClientesSincronizador>.Instance);
        var prodSync = new AwProductosSincronizador(cdb, new AplicarProductoAwService(cdb), prodSp, Options.Create(po),
            TimeProvider.System, NullLogger<AwProductosSincronizador>.Instance);
        var apSp = new ServiceCollection().AddSingleton(cliSync).AddSingleton(prodSync).BuildServiceProvider();
        var aplicador = new AwCambiosAplicador(cdc, db, apSp, Options.Create(new AwCambiosOptions { TamanoLote = 100 }),
            TimeProvider.System, NullLogger<AwCambiosAplicador>.Instance);
        return await aplicador.CicloAsync(entidad, default);
    }

    [Fact]
    public async Task Un_cambio_en_A_W_llega_al_ERP_por_CDC_sin_barrido_y_el_borrado_fisico_no_da_de_baja()
    {
        if (ConnEnv is null) { salida.WriteLine("OMITIDO: falta AW_SANDBOX_CONN."); return; }
        var cdc = new AwCambiosCdcOrigen(new Fabrica(Cadena()), Plomeria, NullLogger<AwCambiosCdcOrigen>.Instance);
        await EsperarCapturaAsync(cdc);

        // 1) Sin watermark: barrido completo inicial y watermark guardado.
        await PrepararAsync(cdc);

        // 2) Sin cambios: ciclo vacío.
        var vacio = await Ciclo(AwEntidadCambio.Cliente, cdc);
        Assert.Equal((0, 0, false), (vacio.Upserts, vacio.Eliminados, vacio.BarridoCompleto));

        // 3) Cambios en A+W: cliente 10 y producto 10 actualizados; cliente 150 y producto 160 borrados físicamente.
        await Script("mutar", "actualizar-cliente");
        await Script("mutar", "actualizar-producto");
        await Script("mutar", "borrar-fila");
        await EsperarCapturaAsync(cdc);

        var rc = await Ciclo(AwEntidadCambio.Cliente, cdc);
        var rp = await Ciclo(AwEntidadCambio.Producto, cdc);
        Assert.Equal((1, 1, false), (rc.Upserts, rc.Eliminados, rc.BarridoCompleto));
        Assert.Equal((1, 1, false), (rp.Upserts, rp.Eliminados, rp.BarridoCompleto));

        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        Assert.Equal("CLIENTE DEMO 010 ACTUALIZADO", (await db.Set<ClienteSincronizacionAw>().AsNoTracking().SingleAsync(r => r.ReferenciaExterna == "10")).NombreComercialOrigen);
        Assert.Equal("PRODUCTO DEMO 010 ACTUALIZADO", (await db.ProductosAw.AsNoTracking().SingleAsync(p => p.ReferenciaExterna == "10")).Descripcion);
        // El borrado físico NO da de baja: ambos siguen activos en el ERP.
        Assert.Equal(EstatusCatalogo.Activo, (await db.Clientes.AsNoTracking().SingleAsync(c => c.Origen == OrigenMaster.Aw && c.ReferenciaExterna == "150")).Estatus);
        Assert.NotNull(await db.ProductosAw.AsNoTracking().SingleOrDefaultAsync(p => p.ReferenciaExterna == "160"));

        // 4) Idempotente: el siguiente ciclo ya no ve nada.
        Assert.Equal(0, (await Ciclo(AwEntidadCambio.Cliente, cdc)).Upserts + (await Ciclo(AwEntidadCambio.Producto, cdc)).Upserts);
    }

    /// <summary>Barrido inicial de ambas entidades con watermark guardado y el ERP poblado (clientes por barrido directo).</summary>
    private async Task PrepararAsync(AwCambiosCdcOrigen cdc)
    {
        var inicialProd = await Ciclo(AwEntidadCambio.Producto, cdc);
        Assert.True(inicialProd.BarridoCompleto);
        await Ciclo(AwEntidadCambio.Cliente, cdc);
        using (var s = factory.Services.CreateScope())
        {
            using var b = s.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var aw = s.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>();
            Assert.Equal(2, await aw.CdcWatermarks.CountAsync());
            // El barrido encolado de clientes lo ejecutaría el dispatcher; aquí se puebla el ERP con un barrido directo.
            await aw.ClientesEjecuciones.Where(e => e.Estado == AwClientesEjecucionEstado.Pendiente)
                .ExecuteUpdateAsync(x => x.SetProperty(e => e.Estado, AwClientesEjecucionEstado.Cancelada));
        }
        await PoblarClientesAsync();
    }

    private async Task<Dictionary<string, string>> WatermarksAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().CdcWatermarks.AsNoTracking()
            .ToDictionaryAsync(w => w.Entidad, w => w.Lsn);
    }

    private static async Task DockerAsync(string verbo)
    {
        using var p = Process.Start(new ProcessStartInfo("docker", [verbo, "aw-sandbox"]) { RedirectStandardOutput = true, RedirectStandardError = true })!;
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidOperationException($"docker {verbo} aw-sandbox falló: {await p.StandardError.ReadToEndAsync()}");
    }

    private static async Task EsperarSaludAsync()
    {
        for (var i = 0; i < 60; i++)
        {
            using var p = Process.Start(new ProcessStartInfo("docker", ["inspect", "-f", "{{.State.Health.Status}}", "aw-sandbox"]) { RedirectStandardOutput = true })!;
            var estado = (await p.StandardOutput.ReadToEndAsync()).Trim();
            await p.WaitForExitAsync();
            if (estado == "healthy") return;
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
        throw new TimeoutException("aw-sandbox no llegó a healthy.");
    }

    [Fact]
    public async Task LSN_expirado_dispara_barrido_completo_de_recuperacion_y_el_watermark_avanza()
    {
        if (ConnEnv is null) { salida.WriteLine("OMITIDO: falta AW_SANDBOX_CONN."); return; }
        var cdc = new AwCambiosCdcOrigen(new Fabrica(Cadena()), Plomeria, NullLogger<AwCambiosCdcOrigen>.Instance);
        await EsperarCapturaAsync(cdc);
        await PrepararAsync(cdc);

        // El producto 10 cambia en A+W y el watermark guardado queda anterior al mínimo de CDC (como tras la limpieza de retención).
        await Script("mutar", "actualizar-producto");
        await EsperarCapturaAsync(cdc);
        var expirado = new string('0', 20);
        using (var s = factory.Services.CreateScope())
            await s.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().CdcWatermarks
                .ExecuteUpdateAsync(x => x.SetProperty(w => w.Lsn, expirado));

        var rp = await Ciclo(AwEntidadCambio.Producto, cdc);
        var rc = await Ciclo(AwEntidadCambio.Cliente, cdc);
        Assert.True(rp.BarridoCompleto);
        Assert.True(rc.BarridoCompleto);

        var wm = await WatermarksAsync();
        Assert.Equal(await cdc.ObtenerLsnActualAsync(default), wm["Producto"]);
        Assert.NotEqual(expirado, wm["Cliente"]);
        using (var s = factory.Services.CreateScope())
        {
            using var b = s.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            // Recuperación por barrido (no por CDC): el cambio llegó al ERP; el de clientes quedó encolado para el dispatcher.
            Assert.Equal("PRODUCTO DEMO 010 ACTUALIZADO",
                (await s.ServiceProvider.GetRequiredService<CompartidoDbContext>().ProductosAw.AsNoTracking().SingleAsync(p => p.ReferenciaExterna == "10")).Descripcion);
            Assert.True(await s.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones
                .AnyAsync(e => e.Tipo == AwClientesEjecucionTipo.Barrido && e.Estado == AwClientesEjecucionEstado.Pendiente));
        }
        // Con el watermark nuevo ya no hay nada pendiente.
        Assert.Equal(0, (await Ciclo(AwEntidadCambio.Producto, cdc)).Upserts);
    }

    [Fact]
    public async Task Fallo_de_A_W_a_mitad_no_avanza_el_watermark_y_al_volver_el_servidor_converge_sin_perder_cambios()
    {
        if (ConnEnv is null) { salida.WriteLine("OMITIDO: falta AW_SANDBOX_CONN."); return; }
        var cdc = new AwCambiosCdcOrigen(new Fabrica(Cadena()), Plomeria, NullLogger<AwCambiosCdcOrigen>.Instance);
        await EsperarCapturaAsync(cdc);
        await PrepararAsync(cdc);

        await Script("mutar", "actualizar-cliente");
        await Script("mutar", "actualizar-producto");
        await EsperarCapturaAsync(cdc);
        var antes = await WatermarksAsync();

        await DockerAsync("stop");
        try
        {
            var ex = await Assert.ThrowsAsync<Millet.Integraciones.Aw.Application.Ports.AwReaderException>(() => Ciclo(AwEntidadCambio.Cliente, cdc));
            Assert.True(ex.IsTransient);
            await Assert.ThrowsAsync<Millet.Integraciones.Aw.Application.Ports.AwReaderException>(() => Ciclo(AwEntidadCambio.Producto, cdc));
            Assert.Equal(antes, await WatermarksAsync()); // no avanzó: los cambios siguen pendientes
        }
        finally
        {
            await DockerAsync("start");
            await EsperarSaludAsync();
        }

        // Reintento: el servidor volvió (sin reseed, la mutación sigue en CDC) y cada cambio se aplica exactamente una vez.
        var rc = await Ciclo(AwEntidadCambio.Cliente, cdc);
        var rp = await Ciclo(AwEntidadCambio.Producto, cdc);
        Assert.Equal((1, 0, false), (rc.Upserts, rc.Eliminados, rc.BarridoCompleto));
        Assert.Equal((1, 0, false), (rp.Upserts, rp.Eliminados, rp.BarridoCompleto));
        Assert.NotEqual(antes["Cliente"], (await WatermarksAsync())["Cliente"]);
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        Assert.Equal("CLIENTE DEMO 010 ACTUALIZADO", (await db.Set<ClienteSincronizacionAw>().AsNoTracking().SingleAsync(r => r.ReferenciaExterna == "10")).NombreComercialOrigen);
        Assert.Equal("PRODUCTO DEMO 010 ACTUALIZADO", (await db.ProductosAw.AsNoTracking().SingleAsync(p => p.ReferenciaExterna == "10")).Descripcion);
    }

    private static async Task<bool> EsperarAsync(Func<Task<bool>> condicion, TimeSpan limite)
    {
        var fin = DateTime.UtcNow + limite;
        while (DateTime.UtcNow < fin)
        {
            if (await condicion()) return true;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        return await condicion();
    }

    /// <summary>
    /// El worker REAL (<see cref="AwCambiosSyncWorker"/>) dentro del host de la API, con los sincronizadores y el CDC reales del sandbox:
    /// nadie llama al aplicador a mano. Un cambio en A+W aparece solo en el ERP; si A+W cae el worker sigue vivo y al volver se pone al día.
    /// </summary>
    [Fact]
    public async Task El_worker_en_el_host_refleja_cambios_de_A_W_solo_sobrevive_a_una_caida_y_se_pone_al_dia()
    {
        if (ConnEnv is null) { salida.WriteLine("OMITIDO: falta AW_SANDBOX_CONN."); return; }
        var cdc = new AwCambiosCdcOrigen(new Fabrica(Cadena()), Plomeria, NullLogger<AwCambiosCdcOrigen>.Instance);
        await EsperarCapturaAsync(cdc);
        await PoblarClientesAsync();

        // Host con el wiring de producción, salvo los orígenes SQL del sandbox (TLS autofirmado: la fábrica de producción lo rechazaría).
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(sv =>
        {
            sv.AddSingleton<IAwCambiosOrigen>(cdc);
            sv.AddScoped<AwCambiosAplicador>();
            sv.Configure<AwCambiosOptions>(o => { o.Habilitado = true; o.IntervaloSegundos = 5; o.TamanoLote = 100; });
            sv.Configure<AwClientesOptions>(o =>
            {
                o.Origen = AwClientesOrigenTipo.Sql; o.LecturaHabilitada = true; o.AplicacionHabilitada = true; o.TamanoLote = 50;
                o.MapeoMoneda = new(StringComparer.OrdinalIgnoreCase) { ["PESOSMX"] = "MXN", ["USD"] = "USD", ["Euro"] = "EUR", ["<indf>"] = "MXN" };
            });
            sv.Configure<AwProductosOptions>(o => { o.OrigenHabilitado = true; o.Origen = AwProductosOrigenTipo.Sql; o.TamanoLote = 50; });
            sv.AddSingleton<IAwClientesOrigen>(sp => new AwClientesSqlOrigen(new Fabrica(Cadena()),
                sp.GetRequiredService<IOptions<AwClientesOptions>>(), NullLogger<AwClientesSqlOrigen>.Instance));
            sv.AddSingleton<IAwProductosOrigen>(sp => new AwProductosSqlOrigen(new Fabrica(Cadena()),
                sp.GetRequiredService<IOptions<AwProductosOptions>>(), NullLogger<AwProductosSqlOrigen>.Instance));
        }));
        var worker = new AwCambiosSyncWorker(host.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AwCambiosOptions { Habilitado = true, IntervaloSegundos = 5 }), NullLogger<AwCambiosSyncWorker>.Instance);
        await worker.StartAsync(default);
        try
        {
            Assert.True(await EsperarAsync(async () => (await WatermarksAsync()).Count == 2, TimeSpan.FromSeconds(120)), "el worker no creó los watermarks");

            // 1) Cambio en A+W -> aparece solo en el ERP.
            await Script("mutar", "actualizar-cliente");
            await Script("mutar", "actualizar-producto");
            Assert.True(await EsperarAsync(async () => await NombreClienteAsync("10") == "CLIENTE DEMO 010 ACTUALIZADO"
                && await DescripcionProductoAsync("10") == "PRODUCTO DEMO 010 ACTUALIZADO", TimeSpan.FromSeconds(120)),
                "el cambio de A+W no llegó al ERP por el worker");

            // 2) A+W cae: el worker no muere (errores aislados, se reintenta).
            await DockerAsync("stop");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15)); // >= 2 ciclos fallidos
                Assert.True(worker.IsRunning, "el worker se detuvo ante la caída de A+W");
            }
            finally
            {
                await DockerAsync("start");
                await EsperarSaludAsync();
            }

            // 3) Vuelve A+W: un cambio posterior se aplica (producto 30 pasa a baja).
            await Script("mutar", "bloquear-producto");
            Assert.True(await EsperarAsync(async () => await EstatusProductoAsync("30") == EstatusCatalogo.Inactivo, TimeSpan.FromSeconds(120)),
                "tras la caída el worker no se puso al día");
            Assert.True(worker.IsRunning);
        }
        finally
        {
            await worker.StopAsync(default);
        }
        Assert.False(worker.IsRunning);
    }

    private async Task<string?> NombreClienteAsync(string referencia)
    {
        using var scope = factory.Services.CreateScope();
        using var b = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return (await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Set<ClienteSincronizacionAw>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.ReferenciaExterna == referencia))?.NombreComercialOrigen;
    }

    private async Task<ProductoAw?> ProductoAsync(string referencia)
    {
        using var scope = factory.Services.CreateScope();
        using var b = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().ProductosAw.AsNoTracking()
            .SingleOrDefaultAsync(p => p.ReferenciaExterna == referencia);
    }

    private async Task<string?> DescripcionProductoAsync(string referencia) => (await ProductoAsync(referencia))?.Descripcion;
    private async Task<EstatusCatalogo?> EstatusProductoAsync(string referencia) => (await ProductoAsync(referencia))?.Estatus;

    private async Task PoblarClientesAsync()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var co = new AwClientesOptions
        {
            Origen = AwClientesOrigenTipo.Sql, LecturaHabilitada = true, AplicacionHabilitada = true, TamanoLote = 50,
            SqlConnectTimeoutSeconds = 5, SqlQueryTimeoutSeconds = 15,
            MapeoMoneda = new(StringComparer.OrdinalIgnoreCase) { ["PESOSMX"] = "MXN", ["USD"] = "USD", ["Euro"] = "EUR", ["<indf>"] = "MXN" },
        };
        var origen = new AwClientesSqlOrigen(new Fabrica(Cadena()), Options.Create(co), NullLogger<AwClientesSqlOrigen>.Instance);
        var s2 = new ServiceCollection().AddSingleton<IAwClientesOrigen>(origen).BuildServiceProvider();
        var sync = new AwClientesSincronizador(sp.GetRequiredService<IntegracionesAwDbContext>(), sp.GetRequiredService<CompartidoDbContext>(),
            sp.GetRequiredService<AplicarClienteAwService>(), s2, Options.Create(co), TimeProvider.System, NullLogger<AwClientesSincronizador>.Instance);
        var id = await sync.IniciarBarridoAsync("test-cdc", default);
        await sync.EjecutarAsync(id, default);
    }
}
