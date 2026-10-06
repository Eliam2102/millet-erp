using System.Data.Common;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Productos;
using Millet.SharedKernel.Application;
using Xunit.Abstractions;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>
/// O1A-AW-INT S2 (productos): <see cref="AwProductosSincronizador"/> con el lector REAL <see cref="AwProductosSqlOrigen"/>
/// contra el sandbox SQL Server (usuario solo-lectura) y PostgreSQL real. OPT-IN por <c>AW_SANDBOX_CONN</c>.
/// Misma colección que los clientes: comparten el sandbox (reseed/mutar/down), así que NO corren en paralelo.
/// Antes y después de cada prueba se borran del destino los ProductoAw de referencia 1..2000 (rango del sandbox).
/// </summary>
[Trait("Category", "AwSandbox")]
[Collection("AwClientesSync")]
public class AwProductosSandboxTests(WebApplicationFactory<Program> factory, ITestOutputHelper salida)
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const int Lote = 50;
    private const int Sembrados = 160;       // BA_PRODUKT 1..160 (el 0 es el registro nulo)
    private const int SinUnidad = 8;         // 34,69,104,139 = '<indf>' y 141..144 sin BEZ idioma 0
    private static readonly string[] Pendientes = ["34", "69", "104", "139", "141", "142", "143", "144"];
    private const string PasswordErroneo = "clave-erronea-QX7";
    private static readonly string? ConnEnv = Environment.GetEnvironmentVariable("AW_SANDBOX_CONN") is { Length: > 0 } v ? v : null;

    private bool _detenido;
    private bool Activo
    {
        get
        {
            if (ConnEnv is null) salida.WriteLine("OMITIDO: falta AW_SANDBOX_CONN (cadena del usuario RO del sandbox).");
            return ConnEnv is not null;
        }
    }

    public async Task InitializeAsync()
    {
        if (ConnEnv is null) return;
        await Script("reseed");
        await LimpiarDestinoAsync();
    }

    public async Task DisposeAsync()
    {
        if (ConnEnv is null) return;
        try { await Script(_detenido ? "up" : "reseed"); }
        finally { await LimpiarDestinoAsync(); }
    }

    // ---------- sandbox ----------
    private static string RaizRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "tools", "aw-sandbox", "aw-sandbox.sh"))) return d.FullName;
        throw new InvalidOperationException("No se encontró tools/aw-sandbox/aw-sandbox.sh subiendo desde " + AppContext.BaseDirectory);
    }

    private static async Task Script(params string[] args)
    {
        var raiz = RaizRepo();
        var psi = new ProcessStartInfo("bash") { WorkingDirectory = raiz, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(Path.Combine(raiz, "tools", "aw-sandbox", "aw-sandbox.sh"));
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var salidaTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await p.WaitForExitAsync(cts.Token);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"aw-sandbox.sh {string.Join(' ', args)} falló ({p.ExitCode}): {await errTask}");
        await salidaTask;
    }

    private sealed class FabricaSandbox(string cadena) : IIntegracionSqlConnectionFactory
    {
        public DbConnection CreateConnection() => new SqlConnection(cadena);
    }

    /// <summary>TrustServerCertificate SOLO aquí (sandbox con certificado autofirmado), nunca en producción.</summary>
    private static string Cadena(string? password = null)
    {
        var b = new SqlConnectionStringBuilder(ConnEnv!)
        {
            Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = true,
            ApplicationIntent = ApplicationIntent.ReadOnly,
        };
        if (password is not null) b.Password = password;
        return b.ConnectionString;
    }

    private static AwProductosOptions Opts(int lote = Lote, params string[] excluidos) => new()
    {
        OrigenHabilitado = true, Origen = AwProductosOrigenTipo.Sql, TamanoLote = lote,
        SqlConnectTimeoutSeconds = 5, SqlQueryTimeoutSeconds = 15, TiposExcluidos = excluidos,
    };

    private static AwProductosSqlOrigen OrigenReal(AwProductosOptions opts, string? password = null) =>
        new(new FabricaSandbox(Cadena(password)), Options.Create(opts), NullLogger<AwProductosSqlOrigen>.Instance);

    private sealed class OrigenObservado(IAwProductosOrigen real) : IAwProductosOrigen
    {
        public Func<int, Task>? AntesDePagina { get; set; }
        public int Paginas { get; private set; }

        public Task<AwProductoOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct) =>
            real.LeerPorReferenciaAsync(referencia, ct);

        public async Task<AwProductosPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct)
        {
            Paginas++;
            if (AntesDePagina is not null) await AntesDePagina(Paginas);
            return await real.LeerPaginaAsync(cursor, tamano, ct);
        }
    }

    // ---------- plomería (patrón de AwProductosSincronizadorTests, con Postgres real) ----------
    private async Task<T> ConSync<T>(IAwProductosOrigen origen, AwProductosOptions opts, Func<AwProductosSincronizador, Task<T>> f)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var sp = new ServiceCollection().AddSingleton(origen).BuildServiceProvider();
        return await f(new AwProductosSincronizador(db, new AplicarProductoAwService(db), sp, Options.Create(opts),
            TimeProvider.System, NullLogger<AwProductosSincronizador>.Instance));
    }

    private Task<AwProductosResumen> Barrido(IAwProductosOrigen origen, AwProductosOptions opts) =>
        ConSync(origen, opts, s => s.SincronizarBarridoAsync(default));

    private Task<AwProductosResumen> Barrido() { var o = Opts(); return Barrido(OrigenReal(o), o); }

    private async Task<T> ConDb<T>(Func<CompartidoDbContext, Task<T>> f)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await f(scope.ServiceProvider.GetRequiredService<CompartidoDbContext>());
    }

    /// <summary>Borra SOLO los ProductoAw de referencia 1..2000 (sandbox) con sus variantes y registros de origen.</summary>
    private async Task LimpiarDestinoAsync() => await ConDb(async db =>
    {
        var refs = Enumerable.Range(1, 2000).Select(i => i.ToString()).ToList();
        var ids = await db.ProductosAw.IgnoreQueryFilters().Where(p => refs.Contains(p.ReferenciaExterna)).Select(p => p.Id).ToListAsync();
        await db.ProductosAwVariantes.Where(v => ids.Contains(v.ProductoAwId)).ExecuteDeleteAsync();
        await db.ProductosSincronizacionAw.Where(r => ids.Contains(r.ProductoAwId)).ExecuteDeleteAsync();
        await db.ProductosAw.IgnoreQueryFilters().Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync();
        return 0;
    });

    private Task<Dictionary<string, ProductoAw>> Destino() => ConDb(async db =>
        (await db.ProductosAw.AsNoTracking().Include(p => p.Variantes).ToListAsync())
        .Where(p => int.TryParse(p.ReferenciaExterna, out var n) && n <= AwConciliacion.MaxReferenciaSandbox)
        .ToDictionary(p => p.ReferenciaExterna));

    private Task<Dictionary<string, ProductoSincronizacionAw>> Registros() => ConDb(async db =>
        (await db.ProductosSincronizacionAw.AsNoTracking().ToListAsync())
        .Where(r => int.TryParse(r.ReferenciaExterna, out var n) && n <= AwConciliacion.MaxReferenciaSandbox)
        .ToDictionary(r => r.ReferenciaExterna));

    private async Task<AwInformeConciliacion> Conciliar(params string[] tiposExcluidos)
    {
        var codigos = await ConDb(db => db.UnidadesMedida.AsNoTracking().Select(u => u.Codigo).ToListAsync());
        // Solo lo que el sincronizador puede crear: con unidad que existe en el catálogo.
        var origen = (await AwConciliacion.LeerOrigenProductosAsync(Cadena(), tiposExcluidos))
            .Where(f => f.Campos["Unidad"] is { } u && codigos.Contains(u)).ToList();
        return AwConciliacion.Comparar(origen, await ConDb(AwConciliacion.LeerDestinoProductosAsync));
    }

    private static string Esperada(AwProductosResumen r) =>
        $"leidos={r.Leidos} creados={r.Creados} act={r.Actualizados} sin={r.SinCambios} pend={r.Pendientes} conf={r.Conflictos} err={r.Errores}";

    // ---------- consulta / recepción ----------
    [Fact]
    public async Task Barrido_completo_pagina_todo_el_origen_y_mapea_los_datos()
    {
        if (!Activo) return;
        var opts = Opts();
        var origen = new OrigenObservado(OrigenReal(opts));
        var r = await Barrido(origen, opts);
        salida.WriteLine(Esperada(r));

        // 160 válidos (el BA_PRODUKT=0 queda fuera); 8 sin unidad equivalente quedan pendientes, sin error.
        Assert.Equal((Sembrados, Sembrados - SinUnidad, SinUnidad, 0), (r.Leidos, r.Creados, r.Pendientes, r.Errores));
        Assert.True(origen.Paginas >= 4, $"se esperaban >=4 páginas de {Lote}, hubo {origen.Paginas}");
        Assert.All(r.ErroresPorReferencia, e => Assert.Equal("UNIDAD_SIN_EQUIVALENCIA", e.Codigo));
        var ps = await Destino();
        Assert.Equal(Sembrados - SinUnidad, ps.Count);
        Assert.DoesNotContain("0", ps.Keys);
        Assert.All(Pendientes, k => Assert.DoesNotContain(k, ps.Keys));

        // Unidades normalizadas: m²->M2, Pza->PZA, m lin.->M, m->M, m³->M3, Kg->KG, ltr->L.
        Assert.Equal("M2", ps["1"].UnidadMedida);
        Assert.Equal("PZA", ps["5"].UnidadMedida);
        Assert.Equal("M", ps["4"].UnidadMedida);
        Assert.Equal("M", ps["155"].UnidadMedida);
        Assert.Equal("M3", ps["156"].UnidadMedida);
        // Descripción con respaldo BA_MCODE (50/100/120 sin descripción; su BA_MCODE es el de la referencia anterior).
        Assert.Equal("PRODUCTO DEMO 049", ps["50"].Descripcion);
        Assert.Equal("PRODUCTO DEMO 099", ps["100"].Descripcion);
        Assert.Equal("PRODUCTO DEMO 010", ps["10"].Descripcion);
        Assert.Equal("PRODUCTO DEMO 028 COLOR DEMO DETALLE DEMO", ps["28"].Descripcion); // BEZ1+BEZ2+BEZ3
        // Composición: laminado, aislante, capas sin espesor ignoradas.
        Assert.Equal("6+0.89+6", ps["9"].Variantes.Single().Composicion);
        Assert.Equal("3+12+3", ps["12"].Variantes.Single().Composicion);
        Assert.Equal("6", ps["15"].Variantes.Single().Composicion);
        // Medidas 0 = sin dato -> null; 8 no tiene ninguna -> sin variante.
        var v9 = ps["9"].Variantes.Single();
        Assert.Equal((null, null, 12.89m), (v9.AltoMm, v9.AnchoMm, v9.EspesorMm));
        Assert.Empty(ps["8"].Variantes);
        var v25 = ps["25"].Variantes.Single();
        Assert.Equal((2400m, 1200m), (v25.AltoMm, v25.AnchoMm));
        Assert.Equal(EstatusCatalogo.Activo, ps["10"].Estatus);

        (await Conciliar()).DebeConciliar();
    }

    // ---------- actualización ----------
    [Fact]
    public async Task Actualizacion_en_origen_se_aplica_y_conserva_fiscales_del_operador()
    {
        if (!Activo) return;
        await Barrido();
        await ConDb(async db =>
        {
            var p = await db.ProductosAw.SingleAsync(x => x.ReferenciaExterna == "10");
            p.AsignarDatosFiscales("43211701", "MTK", "02", 0.16m);
            p.AsignarDatosAduana("70052100", "KGM", 1.5m);
            await db.SaveChangesAsync();
            return 0;
        });

        await Script("mutar", "actualizar-producto");
        var r = await Barrido();
        salida.WriteLine(Esperada(r));

        Assert.Equal((1, Sembrados - SinUnidad - 1, SinUnidad, 0), (r.Actualizados, r.SinCambios, r.Pendientes, r.Errores));
        var p10 = (await Destino())["10"];
        Assert.Equal("PRODUCTO DEMO 010 ACTUALIZADO", p10.Descripcion);
        Assert.Equal((2500m, 1300m), (p10.Variantes.Single().AltoMm, p10.Variantes.Single().AnchoMm));
        Assert.Equal(("43211701", "MTK", "02", 0.16m), (p10.ClaveProdServSat, p10.ClaveUnidadSat, p10.ObjetoImp, p10.TasaIvaTraslado));
        Assert.Equal(("70052100", "KGM", 1.5m), (p10.FraccionArancelaria, p10.UnidadAduana, p10.PesoUnitarioKg));
        (await Conciliar()).DebeConciliar();
    }

    // ---------- duplicado ----------
    [Fact]
    public async Task BA_MCODE_repetido_son_productos_distintos_y_el_barrido_repetido_es_SinCambios()
    {
        if (!Activo) return;
        var origen = await AwConciliacion.LeerSqlAsync(Cadena(), "SELECT CAST(BA_PRODUKT AS nvarchar(20)) AS Ref, BA_MCODE AS Mcode FROM SYSADM.BA_PRODUKTE WHERE BA_PRODUKT > 0");
        var repetidos = origen.GroupBy(f => f.Campos["Mcode"]).Where(g => g.Count() > 1).ToList();
        Assert.NotEmpty(repetidos);
        salida.WriteLine($"BA_MCODE repetidos en origen: {repetidos.Count}");

        await Barrido();
        var ps = await Destino();
        Assert.NotEqual(ps["9"].Id, ps["10"].Id); // 10 tiene el BA_MCODE de 9
        Assert.Equal(ps.Count, ps.Values.Select(p => p.ReferenciaExterna).Distinct().Count());

        var r = await Barrido();
        Assert.Equal((Sembrados, 0, 0, Sembrados - SinUnidad, SinUnidad), (r.Leidos, r.Creados, r.Actualizados, r.SinCambios, r.Pendientes));
        Assert.Equal(ps.Count, (await Destino()).Count);
        Assert.Equal(ps.Count, (await Registros()).Count);
    }

    // ---------- dato inválido ----------
    [Fact]
    public async Task Unidad_desconocida_requiere_revision_sin_persistir_y_sin_pisar_al_existente()
    {
        if (!Activo) return;
        await Script("mutar", "unidad-desconocida"); // producto 40 -> 'xyz'
        var r = await Barrido();
        Assert.Equal((SinUnidad + 1, 0), (r.Pendientes, r.Errores));
        Assert.Contains(r.ErroresPorReferencia, e => e.Referencia == "40" && e.Codigo == "UNIDAD_SIN_EQUIVALENCIA");
        Assert.DoesNotContain("40", (await Destino()).Keys);          // alta: no persiste nada
        Assert.DoesNotContain("40", (await Registros()).Keys);

        await LimpiarDestinoAsync();
        await Script("reseed");
        await Barrido();                                               // 40 existe con M2
        await Script("mutar", "unidad-desconocida");
        var r2 = await Barrido();
        Assert.Equal(SinUnidad + 1, r2.Pendientes);
        var p40 = (await Destino())["40"];
        Assert.Equal("M2", p40.UnidadMedida);                          // producto intacto
        var reg = (await Registros())["40"];
        Assert.Equal(ResultadoSincronizacionAw.Pendiente, reg.Resultado);
        Assert.StartsWith("UNIDAD_SIN_EQUIVALENCIA", reg.Error);
    }

    // ---------- baja ----------
    [Fact]
    public async Task Bloqueo_en_origen_da_de_baja_sin_borrar_y_la_ausencia_de_fila_no_es_baja()
    {
        if (!Activo) return;
        await Barrido();
        var antes = await Destino();
        Assert.Equal(EstatusCatalogo.Activo, antes["30"].Estatus);
        Assert.Equal(EstatusCatalogo.Inactivo, antes["4"].Estatus);    // sembrado con KZ_GESPERRT=1: alta ya inactiva
        Assert.NotNull(antes["4"].FechaBaja);

        await Script("mutar", "bloquear-producto"); // 30
        var r = await Barrido();
        Assert.Equal(1, r.Actualizados);
        var p30 = (await Destino())["30"];
        Assert.Equal(EstatusCatalogo.Inactivo, p30.Estatus);
        Assert.NotNull(p30.FechaBaja);
        Assert.Equal("1", (await Registros())["30"].BajaOrigenCruda);

        await Script("mutar", "borrar-fila"); // producto 160 desaparece del origen
        var r2 = await Barrido();
        Assert.Equal((Sembrados - 1, 0, 0), (r2.Leidos, r2.Actualizados, r2.Errores));
        var despues = await Destino();
        Assert.Equal(antes.Count, despues.Count);                      // nunca DELETE
        Assert.Equal(EstatusCatalogo.Activo, despues["160"].Estatus);  // ausencia != baja
        Assert.Null(despues["160"].FechaBaja);

        var porRef = await ConSync(OrigenReal(Opts()), Opts(), s => s.SincronizarReferenciaAsync("160", default));
        Assert.Equal("no_encontrada_en_origen", Assert.Single(porRef.ErroresPorReferencia).Codigo);
        Assert.Equal(EstatusCatalogo.Activo, (await Destino())["160"].Estatus);
    }

    // ---------- conflicto de versión ----------
    [Fact]
    public async Task Edicion_manual_concurrente_da_Conflicto_sin_sobrescribir()
    {
        if (!Activo) return;
        // (a) producto manual con la misma referencia no se apropia.
        await ConDb(async db =>
        {
            db.ProductosAw.Add(new ProductoAw(Guid.NewGuid(), "20", "EDICION MANUAL", "M2", OrigenMaster.Manual));
            await db.SaveChangesAsync();
            return 0;
        });
        var r = await Barrido();
        Assert.Equal(1, r.Conflictos);
        Assert.Equal(Sembrados - SinUnidad - 1, r.Creados);
        Assert.Equal("EDICION MANUAL", await ConDb(db => db.ProductosAw.Where(p => p.ReferenciaExterna == "20").Select(p => p.Descripcion).SingleAsync()));

        // (b) If-Match: otro editor sube la Version tras la lectura -> no se escribe.
        var versionLeida = (await Destino())["10"].Version;
        await ConDb(async db =>
        {
            (await db.ProductosAw.SingleAsync(p => p.ReferenciaExterna == "10")).ActualizarDatos(descripcion: "EDITADA A MANO");
            await db.SaveChangesAsync();
            return 0;
        });
        await Script("mutar", "actualizar-producto");
        var o = Opts();
        var rc = await ConSync(OrigenReal(o), o, s => s.SincronizarReferenciaAsync("10", default, versionLeida));
        Assert.Equal((1, 0), (rc.Conflictos, rc.Actualizados));
        Assert.Equal("EDITADA A MANO", (await Destino())["10"].Descripcion);

        // Con la versión vigente sí aplica.
        var vigente = (await Destino())["10"].Version;
        var ok = await ConSync(OrigenReal(o), o, s => s.SincronizarReferenciaAsync("10", default, vigente));
        Assert.Equal((0, 1), (ok.Conflictos, ok.Actualizados));
        Assert.Equal("PRODUCTO DEMO 010 ACTUALIZADO", (await Destino())["10"].Descripcion);
    }

    // ---------- tipos excluidos ----------
    [Fact]
    public async Task TiposExcluidos_filtra_por_BA_PRODUKTART_en_el_origen()
    {
        if (!Activo) return;
        string[] excl = ["Proceso", "Servicios/recargos"];
        var total = (await AwConciliacion.LeerSqlAsync(Cadena(), "SELECT CAST(BA_PRODUKT AS nvarchar(20)) FROM SYSADM.BA_PRODUKTE WHERE BA_PRODUKT > 0")).Count;
        var esperados = (await AwConciliacion.LeerOrigenProductosAsync(Cadena(), excl)).Count;
        Assert.InRange(esperados, 1, total - 1);

        var o = Opts(Lote, excl);
        var r = await Barrido(OrigenReal(o), o);
        salida.WriteLine($"{Esperada(r)} (origen sin tipos excluidos={esperados} de {total})");
        Assert.Equal(esperados, r.Leidos);
        Assert.Equal(r.Leidos, r.Creados + r.Pendientes);
        (await Conciliar(excl)).DebeConciliar();
    }

    // ---------- fallo y reintento ----------
    [Fact]
    public async Task Origen_caido_a_mitad_clasifica_el_error_sin_duplicados_y_el_reintento_converge()
    {
        if (!Activo) return;
        var opts = Opts();
        var origen = new OrigenObservado(OrigenReal(opts));
        origen.AntesDePagina = async n => { if (n == 2) { _detenido = true; await Script("down"); } };
        var ex = await Assert.ThrowsAsync<AwReaderException>(() => Barrido(origen, opts));
        Assert.True(ex.IsTransient);
        salida.WriteLine($"Kind con el contenedor parado: {ex.Kind}");

        var parcial = await Destino();
        Assert.Equal(Lote - 1, parcial.Count); // 1ª página aplicada (50 menos el 34 sin unidad), nada duplicado
        Assert.Equal(parcial.Count, (await Registros()).Count);

        // Reintento por referencia mientras sigue caído: error clasificado, nada nuevo.
        await Assert.ThrowsAsync<AwReaderException>(() => ConSync(OrigenReal(opts), opts, s => s.SincronizarReferenciaAsync("100", default)));
        Assert.Equal(parcial.Count, (await Destino()).Count);

        await Script("up");
        _detenido = false;
        var r1 = await ConSync(OrigenReal(opts), opts, s => s.SincronizarReferenciaAsync("100", default));
        Assert.Equal((1, 1), (r1.Leidos, r1.Creados));

        var r = await Barrido();
        Assert.Equal(Sembrados, r.Leidos);
        Assert.Equal(Lote - 1 + 1, r.SinCambios);                       // las 49 del 1er intento + la 100
        Assert.Equal(Sembrados - SinUnidad, r.Creados + r.SinCambios + r.Actualizados);
        (await Conciliar()).DebeConciliar();
    }

    [Fact]
    public async Task Credenciales_erroneas_dan_error_clasificado_sin_filtrar_la_contrasena()
    {
        if (!Activo) return;
        var opts = Opts();
        var ex = await Assert.ThrowsAsync<AwReaderException>(() => Barrido(OrigenReal(opts, PasswordErroneo), opts));
        Assert.Equal("auth", ex.Kind);
        Assert.False(ex.IsTransient);
        Assert.DoesNotContain(PasswordErroneo, ex.Message);
        Assert.DoesNotContain(PasswordErroneo, ex.ToString());
        Assert.Empty(await Destino());
    }

    // ---------- conciliación ----------
    [Fact]
    public async Task Conciliacion_reporta_faltantes_sobrantes_y_diferencias_explicitos()
    {
        if (!Activo) return;
        await Barrido();
        var informe = await Conciliar();
        salida.WriteLine(informe.ToString());
        informe.DebeConciliar();
        Assert.Equal(Sembrados - SinUnidad, informe.ConteoOrigen);

        await Script("mutar", "actualizar-producto"); // origen cambia, sin barrido
        var dif = await Conciliar();
        Assert.False(dif.Conciliado);
        Assert.Contains(dif.Diferencias, d => d.Contains("ref 10 campo Descripcion") && d.Contains("ACTUALIZADO"));
        Assert.Contains(dif.Diferencias, d => d.Contains("ref 10 campo Alto") && d.Contains("2500"));
        Assert.Empty(dif.Faltantes); Assert.Empty(dif.Sobrantes);

        await Script("mutar", "borrar-fila"); // 160 sale del origen; el destino lo conserva
        Assert.Equal(["160"], (await Conciliar()).Sobrantes);

        await ConDb(async db => // se pierde el 30 del destino
        {
            var id = await db.ProductosAw.Where(p => p.ReferenciaExterna == "30").Select(p => p.Id).SingleAsync();
            await db.ProductosAwVariantes.Where(v => v.ProductoAwId == id).ExecuteDeleteAsync();
            await db.ProductosSincronizacionAw.Where(r => r.ProductoAwId == id).ExecuteDeleteAsync();
            await db.ProductosAw.Where(p => p.Id == id).ExecuteDeleteAsync();
            return 0;
        });
        var falt = await Conciliar();
        Assert.Equal(["30"], falt.Faltantes);
        Assert.Equal(["160"], falt.Sobrantes);
    }
}
