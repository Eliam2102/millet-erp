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
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Clientes;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.SharedKernel.Application;
using Xunit.Abstractions;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>
/// O1A-AW-INT S2: <see cref="AwClientesSincronizador"/> con el lector REAL <see cref="AwClientesSqlOrigen"/> contra
/// el sandbox SQL Server (<c>tools/aw-sandbox</c>, usuario solo-lectura) y PostgreSQL real. OPT-IN: sin la variable
/// <c>AW_SANDBOX_CONN</c> (cadena del usuario RO) cada prueba retorna temprano con mensaje.
/// Las mutaciones/paradas del sandbox pasan por <c>aw-sandbox.sh</c> (el usuario RO no escribe) y se restauran al final.
/// Los clientes sincronizados (referencias 1..150, 1001) se borran del destino antes y después de cada prueba.
/// </summary>
[Trait("Category", "AwSandbox")]
[Collection("AwClientesSync")]
public class AwClientesSandboxTests(WebApplicationFactory<Program> factory, ITestOutputHelper salida)
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const int Lote = 50;
    private const int FilasSembradas = 150;
    private const string PasswordErroneo = "clave-erronea-QX7";
    private static readonly string? ConnEnv = Environment.GetEnvironmentVariable("AW_SANDBOX_CONN") is { Length: > 0 } v ? v : null;

    private bool _detenido; // el contenedor quedó parado: la limpieza hace `up` en vez de `reseed`
    private bool Activo
    {
        get
        {
            if (ConnEnv is null) salida.WriteLine("OMITIDO: falta AW_SANDBOX_CONN (cadena del usuario RO del sandbox).");
            return ConnEnv is not null;
        }
    }

    // ---------- ciclo de vida: sandbox y destino limpios por prueba ----------
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

    // ---------- origen real ----------
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

    private static AwClientesOptions Opts(bool mapeaIndf = true, int lote = Lote)
    {
        var o = new AwClientesOptions
        {
            Origen = AwClientesOrigenTipo.Sql, LecturaHabilitada = true, AplicacionHabilitada = true, TamanoLote = lote,
            SqlConnectTimeoutSeconds = 5, SqlQueryTimeoutSeconds = 15,
            MapeoMoneda = new(StringComparer.OrdinalIgnoreCase) { ["PESOSMX"] = "MXN", ["USD"] = "USD", ["Euro"] = "EUR" },
        };
        // '<indf>' es el registro nulo de A+W; mapearlo (solo en pruebas) deja que casi todos los clientes se creen.
        if (mapeaIndf) o.MapeoMoneda["<indf>"] = "MXN";
        return o;
    }

    private static AwClientesSqlOrigen OrigenReal(AwClientesOptions opts, string? password = null) =>
        new(new FabricaSandbox(Cadena(password)), Options.Create(opts), NullLogger<AwClientesSqlOrigen>.Instance);

    /// <summary>Envuelve el origen real para actuar justo antes de leer una página (p. ej. parar el contenedor).</summary>
    private sealed class OrigenObservado(IAwClientesOrigen real) : IAwClientesOrigen
    {
        public Func<int, Task>? AntesDePagina { get; set; }
        public int Paginas { get; private set; }

        public Task<AwClienteOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct) =>
            real.LeerPorReferenciaAsync(referencia, ct);

        public async Task<AwClientesPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct)
        {
            Paginas++;
            if (AntesDePagina is not null) await AntesDePagina(Paginas);
            return await real.LeerPaginaAsync(cursor, tamano, ct);
        }
    }

    // ---------- plomería de pruebas (patrón de AwClientesSincronizadorTests) ----------
    private async Task<T> ConSync<T>(IAwClientesOrigen origen, AwClientesOptions opts,
        Func<AwClientesSincronizador, Task<T>> f)
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var services = new ServiceCollection();
        services.AddSingleton(origen);
        var sync = new AwClientesSincronizador(sp.GetRequiredService<IntegracionesAwDbContext>(),
            sp.GetRequiredService<CompartidoDbContext>(), sp.GetRequiredService<AplicarClienteAwService>(),
            services.BuildServiceProvider(), Options.Create(opts), TimeProvider.System,
            NullLogger<AwClientesSincronizador>.Instance);
        return await f(sync);
    }

    private async Task LimpiarVivosAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones
            .Where(e => e.Estado == AwClientesEjecucionEstado.Pendiente || e.Estado == AwClientesEjecucionEstado.EnCurso)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Estado, AwClientesEjecucionEstado.Cancelada));
    }

    /// <summary>Borra del destino SOLO los clientes Aw de referencia ≤ 100000 (sandbox) y sus registros de origen.</summary>
    private async Task LimpiarDestinoAsync()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var refs = Enumerable.Range(1, 2000).Select(i => i.ToString()).ToList();
        await db.Set<ClienteSincronizacionAw>().Where(r => refs.Contains(r.ReferenciaExterna)).ExecuteDeleteAsync();
        await db.Clientes.IgnoreQueryFilters().Where(c => c.Origen == OrigenMaster.Aw && c.ReferenciaExterna != null && refs.Contains(c.ReferenciaExterna))
            .ExecuteDeleteAsync();
    }

    private async Task<AwClientesEjecucion> Barrido(IAwClientesOrigen origen, AwClientesOptions opts)
    {
        await LimpiarVivosAsync();
        var id = await ConSync(origen, opts, async s =>
        {
            var i = await s.IniciarBarridoAsync("test-sandbox", CancellationToken.None);
            await s.EjecutarAsync(i, CancellationToken.None);
            return i;
        });
        return await Ejec(id);
    }

    private async Task<AwClientesEjecucion> Ejec(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones
            .AsNoTracking().Include(e => e.ErroresPorReferencia).SingleAsync(e => e.Id == id);
    }

    private async Task<(Dictionary<string, Cliente> Clientes, Dictionary<string, ClienteSincronizacionAw> Regs)> Destino()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var cs = (await db.Clientes.AsNoTracking().Where(c => c.Origen == OrigenMaster.Aw && c.ReferenciaExterna != null).ToListAsync())
            .Where(c => int.Parse(c.ReferenciaExterna!) <= AwConciliacion.MaxReferenciaSandbox).ToDictionary(c => c.ReferenciaExterna!);
        var regs = (await db.Set<ClienteSincronizacionAw>().AsNoTracking().ToListAsync())
            .Where(r => cs.ContainsKey(r.ReferenciaExterna)).ToDictionary(r => r.ReferenciaExterna);
        return (cs, regs);
    }

    private async Task<AwInformeConciliacion> Conciliar(Func<AwFilaConciliable, bool>? filtroOrigen = null)
    {
        var origen = (await AwConciliacion.LeerOrigenClientesAsync(Cadena())).Where(filtroOrigen ?? (_ => true)).ToList();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return AwConciliacion.Comparar(origen, await AwConciliacion.LeerDestinoClientesAsync(scope.ServiceProvider.GetRequiredService<CompartidoDbContext>()));
    }

    /// <summary>Filas que el sincronizador crea con el mapeo completo: NAME1 no vacío (la 140 sembrada es inválida).</summary>
    private static bool Creable(AwFilaConciliable f) => !string.IsNullOrEmpty(f.Campos["Nombre"]);

    // ---------- consulta / recepción ----------
    [Fact]
    public async Task Barrido_completo_pagina_todo_el_origen_y_mapea_los_datos()
    {
        if (!Activo) return;
        var origen = new OrigenObservado(OrigenReal(Opts()));
        var e = await Barrido(origen, Opts());

        // 150 sembrados; el 140 trae NAME1 vacío -> fila_invalida (único error). Rows sin duplicados.
        Assert.Equal(AwClientesEjecucionEstado.Parcial, e.Estado);
        Assert.Equal((FilasSembradas, FilasSembradas - 1, 1), (e.Leidos, e.Creados, e.Errores));
        Assert.True(origen.Paginas >= 4, $"se esperaban >=4 páginas de {Lote} (150 filas + página final), hubo {origen.Paginas}");
        Assert.Equal("fila_invalida", Assert.Single(e.ErroresPorReferencia).Codigo);
        var (cs, regs) = await Destino();
        Assert.Equal(FilasSembradas - 1, cs.Count);
        Assert.Equal(cs.Count, regs.Count);
        Assert.All(cs.Values, c => Assert.Equal(EstatusCatalogo.Activo, c.Estatus));

        // Datos mapeados: 10 = caso normal; 7 = USD; 77 = Euro; 19 = '30 DIAS' coincidencia única.
        var c10 = cs["10"]; var r10 = regs["10"];
        Assert.Equal(("AW-10", "CLIENTE DEMO 010", "MXN", "5500000010"), (c10.Clave, c10.RazonSocial, c10.MonedaDefault, c10.Telefono));
        Assert.Equal(("CONTADO", 1, 0, "PESOSMX", "MXN"), (r10.CondicionCodigoOrigen, r10.CondicionNumeroOrigen, r10.DiasNominalesOrigen, r10.MonedaCodigoOrigen, r10.MonedaNormalizada));
        Assert.Equal("USD", cs["7"].MonedaDefault);
        Assert.Equal("EUR", cs["77"].MonedaDefault);
        Assert.Equal(("30 DIAS", 6, 30), (regs["19"].CondicionCodigoOrigen, regs["19"].CondicionNumeroOrigen, regs["19"].DiasNominalesOrigen));
        Assert.Null(c10.Rfc); // nunca se deriva RFC de UST_ID/STEUERNUMMER
        Assert.Equal("DEMO010", r10.CandidatoFiscalUstId);

        // Segundo barrido: idempotente.
        var e2 = await Barrido(OrigenReal(Opts()), Opts());
        Assert.Equal((FilasSembradas, 0, 0, FilasSembradas - 1), (e2.Leidos, e2.Creados, e2.Actualizados, e2.SinCambios));
        Assert.Equal(FilasSembradas - 1, (await Destino()).Clientes.Count);
    }

    // ---------- actualización ----------
    [Fact]
    public async Task Actualizacion_en_origen_se_aplica_en_el_segundo_barrido_y_conserva_fiscales()
    {
        if (!Activo) return;
        await Barrido(OrigenReal(Opts()), Opts());
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            (await db.Clientes.SingleAsync(c => c.ReferenciaExterna == "10")).ActualizarDatos(rfc: "AAA010101AAA", regimenFiscal: "601", codigoPostalFiscal: "44100");
            await db.SaveChangesAsync();
        }

        await Script("mutar", "actualizar-cliente");
        var e = await Barrido(OrigenReal(Opts()), Opts());

        Assert.Equal((0, 1, FilasSembradas - 2, 1), (e.Creados, e.Actualizados, e.SinCambios, e.Errores));
        var (cs, regs) = await Destino();
        Assert.Equal("CLIENTE DEMO 010 ACTUALIZADO", regs["10"].NombreComercialOrigen);
        Assert.Equal(("AAA010101AAA", "601", "44100", "CLIENTE DEMO 010"), (cs["10"].Rfc, cs["10"].RegimenFiscal, cs["10"].CodigoPostalFiscal, cs["10"].RazonSocial));
        (await Conciliar(Creable)).DebeConciliar();
    }

    // ---------- duplicado ----------
    [Fact]
    public async Task RFC_generico_repetido_son_dos_clientes_y_el_barrido_repetido_es_SinCambios()
    {
        if (!Activo) return;
        await Barrido(OrigenReal(Opts()), Opts());
        var (cs0, regs0) = await Destino();
        // XAXX010101000 (UST_ID) repetido en 3, 6, 9...: clientes distintos que comparten candidato; Rfc local intacto (null).
        Assert.Equal(regs0["3"].CandidatoFiscalUstId, regs0["6"].CandidatoFiscalUstId);
        Assert.Equal("XAXX010101000", regs0["3"].CandidatoFiscalUstId);
        Assert.NotEqual(cs0["3"].Id, cs0["6"].Id);
        Assert.Null(cs0["3"].Rfc);

        await Script("mutar", "duplicar-cliente"); // 1001 copia de 5 (mismo UST_ID, otro ID)
        var e = await Barrido(OrigenReal(Opts()), Opts());
        Assert.Equal((FilasSembradas + 1, 1, 0, FilasSembradas - 1), (e.Leidos, e.Creados, e.Actualizados, e.SinCambios));
        var (cs, regs) = await Destino();
        Assert.Equal(regs["5"].CandidatoFiscalUstId, regs["1001"].CandidatoFiscalUstId);
        Assert.NotEqual(cs["5"].Id, cs["1001"].Id);

        var e3 = await Barrido(OrigenReal(Opts()), Opts()); // repetido
        Assert.Equal((FilasSembradas + 1, 0, 0, FilasSembradas), (e3.Leidos, e3.Creados, e3.Actualizados, e3.SinCambios));
        Assert.Equal(cs.Count, (await Destino()).Clientes.Count);
    }

    // ---------- dato inválido ----------
    [Fact]
    public async Task Moneda_y_estado_invalidos_quedan_pendientes_sin_baja()
    {
        if (!Activo) return;
        await Barrido(OrigenReal(Opts()), Opts());
        var antes = (await Destino()).Clientes;

        await Script("mutar", "invalidar-moneda"); // 20 -> XYZ
        await Script("mutar", "invalidar-estado"); // 21 -> KZ_STATUS 9
        var e = await Barrido(OrigenReal(Opts()), Opts());

        Assert.Equal((2, 1), (e.Actualizados, e.Errores)); // el único error sigue siendo la fila 140
        var (cs, regs) = await Destino();
        Assert.Equal(("XYZ", null), (regs["20"].MonedaCodigoOrigen, regs["20"].MonedaNormalizada));
        Assert.Equal(ResultadoSincronizacionAw.Pendiente, regs["20"].Resultado);
        Assert.Equal(9, regs["21"].EstadoOrigenCrudo);
        Assert.Equal(ResultadoSincronizacionAw.Pendiente, regs["21"].Resultado);
        foreach (var r in new[] { "20", "21" })
        {
            Assert.Equal(EstatusCatalogo.Activo, cs[r].Estatus); // sin baja automática
            Assert.Equal(antes[r].MonedaDefault, cs[r].MonedaDefault); // sin cambio de moneda
        }
        Assert.Equal(antes.Count, cs.Count);
    }

    [Fact]
    public async Task Moneda_sin_equivalencia_en_alta_no_crea_el_cliente_y_lo_reporta()
    {
        if (!Activo) return;
        await Script("mutar", "invalidar-moneda");
        var o = Opts(mapeaIndf: false);
        var e = await Barrido(OrigenReal(o), o);

        var origen = await AwConciliacion.LeerOrigenClientesAsync(Cadena());
        var sinEquiv = origen.Where(f => f.Campos["Moneda"] is "<indf>" or "XYZ").Select(f => f.Referencia).ToHashSet();
        Assert.Contains("20", sinEquiv);
        var cod = e.ErroresPorReferencia.ToDictionary(x => x.Referencia, x => x.Codigo);
        Assert.Equal("moneda_sin_equivalencia", cod["20"]);
        Assert.Equal(sinEquiv.Count, cod.Count(kv => kv.Value == "moneda_sin_equivalencia"));
        var (cs, _) = await Destino();
        Assert.DoesNotContain("20", cs.Keys);
        Assert.Equal(FilasSembradas - sinEquiv.Count - 1, cs.Count); // -1: la 140 (NAME1 vacío)
    }

    [Fact]
    public async Task Cambio_de_dias_en_catalogo_cambia_el_hash_solo_de_los_clientes_afectados()
    {
        if (!Activo) return;
        await Barrido(OrigenReal(Opts()), Opts());
        var antes = (await Destino()).Regs;
        var afectados = antes.Values.Where(r => r.CondicionCodigoOrigen == "30 DIAS").Select(r => r.ReferenciaExterna).ToHashSet();
        Assert.NotEmpty(afectados);

        await Script("mutar", "cambiar-dias-catalogo"); // '30 DIAS': 30 -> 35
        var e = await Barrido(OrigenReal(Opts()), Opts());

        Assert.Equal(afectados.Count, e.Actualizados);
        var despues = (await Destino()).Regs;
        foreach (var (r, reg) in despues)
        {
            if (afectados.Contains(r))
            {
                Assert.NotEqual(antes[r].HashOrigen, reg.HashOrigen);
                Assert.Equal(35, reg.DiasNominalesOrigen);
            }
            else Assert.Equal(antes[r].HashOrigen, reg.HashOrigen);
        }
    }

    [Fact]
    public async Task ZAHLBED_sin_coincidencia_en_catalogo_queda_pendiente_sin_duplicar()
    {
        if (!Activo) return;
        await Barrido(OrigenReal(Opts()), Opts());
        var (cs, regs) = await Destino();
        // 47 = 'CREDITO DEMO' (no existe en KA_ZAHLBED); 53 = 'contado' (minúscula: CS_AS no coincide).
        foreach (var r in new[] { "47", "53" })
        {
            Assert.Null(regs[r].CondicionNumeroOrigen);
            Assert.Null(regs[r].DiasNominalesOrigen);
            Assert.Equal(ResultadoSincronizacionAw.Pendiente, regs[r].Resultado);
            Assert.Equal(EstatusCatalogo.Activo, cs[r].Estatus);
        }
        Assert.Equal("CREDITO DEMO", regs["47"].CondicionCodigoOrigen);
        var e2 = await Barrido(OrigenReal(Opts()), Opts());
        Assert.Equal(0, e2.Creados);
        Assert.Equal(cs.Count, (await Destino()).Clientes.Count);
    }

    // ---------- fallo y reintento ----------
    [Fact]
    public async Task Origen_caido_a_mitad_deja_Fallida_clasificada_sin_duplicados_y_el_reintento_converge()
    {
        if (!Activo) return;
        var opts = Opts();
        var origen = new OrigenObservado(OrigenReal(opts));
        origen.AntesDePagina = async n => { if (n == 2) { _detenido = true; await Script("down"); } };
        var e = await Barrido(origen, opts);

        Assert.Equal(AwClientesEjecucionEstado.Fallida, e.Estado);
        Assert.Equal("lectura_origen_fallida (AwReaderException)", e.ErrorGeneral); // clasificado, sin ex.Message
        Assert.Equal(Lote, e.Creados); // la 1ª página quedó aplicada
        var (cs, regs) = await Destino();
        Assert.Equal(Lote, cs.Count);
        Assert.Equal(Lote, regs.Count);

        // El lector clasifica la causa (contenedor parado): AwReaderException transitoria de conexión.
        var ex = await Assert.ThrowsAsync<AwReaderException>(() => OrigenReal(opts).LeerPaginaAsync(null, Lote, default));
        Assert.True(ex.IsTransient);
        salida.WriteLine($"Kind con el contenedor parado: {ex.Kind}");

        // Reintento por referencia mientras sigue caído: Fallida clasificada, nada nuevo.
        var idRef = await ConSync(OrigenReal(opts), opts, s => s.ReintentarReferenciaAsync("100", "test-sandbox", default));
        Assert.Equal(AwClientesEjecucionEstado.Fallida, (await Ejec(idRef)).Estado);

        await Script("up");
        _detenido = false;

        // Reintento por referencia ya con el origen arriba.
        var idRef2 = await ConSync(OrigenReal(opts), opts, s => s.ReintentarReferenciaAsync("100", "test-sandbox", default));
        Assert.Equal((AwClientesEjecucionEstado.Completa, 1), ((await Ejec(idRef2)).Estado, (await Ejec(idRef2)).Creados));

        // Nuevo barrido: converge (las ya aplicadas SinCambios, el resto Creado) y concilia.
        var e2 = await Barrido(OrigenReal(opts), opts);
        Assert.Equal(FilasSembradas, e2.Leidos);
        Assert.Equal(FilasSembradas - 1, e2.Creados + e2.SinCambios + e2.Actualizados);
        Assert.Equal(Lote + 1, e2.SinCambios + e2.Actualizados); // 50 del 1er intento + la 100
        (await Conciliar(Creable)).DebeConciliar();
    }

    [Fact]
    public async Task Credenciales_erroneas_dan_error_clasificado_sin_filtrar_la_contrasena()
    {
        if (!Activo) return;
        var opts = Opts();
        var malo = OrigenReal(opts, PasswordErroneo);
        var ex = await Assert.ThrowsAsync<AwReaderException>(() => malo.LeerPaginaAsync(null, Lote, default));
        Assert.Equal("auth", ex.Kind);
        Assert.False(ex.IsTransient);
        Assert.DoesNotContain(PasswordErroneo, ex.Message);
        Assert.DoesNotContain(PasswordErroneo, ex.ToString());

        var e = await Barrido(malo, opts);
        Assert.Equal(AwClientesEjecucionEstado.Fallida, e.Estado);
        Assert.Equal("lectura_origen_fallida (AwReaderException)", e.ErrorGeneral);
        Assert.DoesNotContain(PasswordErroneo, e.ErrorGeneral);
        Assert.Empty((await Destino()).Clientes);
    }

    // ---------- conciliación ----------
    [Fact]
    public async Task Conciliacion_concilia_tras_barrido_y_reporta_faltantes_sobrantes_y_diferencias()
    {
        if (!Activo) return;
        await Barrido(OrigenReal(Opts()), Opts());
        var informe = await Conciliar(Creable);
        salida.WriteLine(informe.ToString());
        informe.DebeConciliar();
        Assert.Equal(FilasSembradas - 1, informe.ConteoOrigen);

        // Diferencia: el origen cambia y aún no hay barrido.
        await Script("mutar", "actualizar-cliente");
        var dif = await Conciliar(Creable);
        Assert.False(dif.Conciliado);
        Assert.Contains(dif.Diferencias, d => d.Contains("ref 10 campo Nombre") && d.Contains("ACTUALIZADO"));
        Assert.Empty(dif.Faltantes); Assert.Empty(dif.Sobrantes);

        // Sobrante: 150 desaparece del origen; ausencia no es baja, el destino la conserva.
        await Script("mutar", "borrar-fila");
        var sob = await Conciliar(Creable);
        Assert.Equal(["150"], sob.Sobrantes);

        // Faltante: se pierde un cliente del destino.
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            await db.Set<ClienteSincronizacionAw>().Where(r => r.ReferenciaExterna == "30").ExecuteDeleteAsync();
            await db.Clientes.IgnoreQueryFilters().Where(c => c.ReferenciaExterna == "30" && c.Origen == OrigenMaster.Aw).ExecuteDeleteAsync();
        }
        var falt = await Conciliar(Creable);
        Assert.Equal(["30"], falt.Faltantes);
        Assert.Equal(["150"], falt.Sobrantes);
    }
}
