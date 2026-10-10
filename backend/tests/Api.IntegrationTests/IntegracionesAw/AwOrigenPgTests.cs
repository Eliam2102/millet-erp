using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Npgsql;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>
/// Origen de DEMO en PostgreSQL (<c>tools/aw-origen-demo</c>): los lectores <see cref="AwClientesPgOrigen"/> y
/// <see cref="AwProductosPgOrigen"/> con los sincronizadores REALES contra el PostgreSQL de la prueba. El esquema y el
/// seeder son los mismos archivos que usa la demo (<c>schema.sql</c> + <c>seed.sql</c>), aplicados aquí en el esquema
/// <c>aw_origen</c> de la BD de la prueba y borrados al terminar. Los clientes/productos sincronizados (referencias
/// 1..2000, mismo rango que las pruebas de sandbox) se borran del destino antes y después de cada prueba.
/// </summary>
[Collection("AwClientesSync")]
public class AwOrigenPgTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const int Lote = 25;
    private const int Clientes = 60, Productos = 47;
    private string _cs = "";

    // ---------- ciclo de vida: esquema + seeder del origen y destino limpios ----------
    public async Task InitializeAsync()
    {
        _cs = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")!;
        var dir = DirectorioOrigenDemo();
        await using var cn = new NpgsqlConnection(_cs);
        await cn.OpenAsync();
        foreach (var archivo in new[] { "schema.sql", "seed.sql" })
        {
            await using var cmd = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(dir, archivo)), cn);
            await cmd.ExecuteNonQueryAsync();
        }
        await LimpiarDestinoAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            await using var cn = new NpgsqlConnection(_cs);
            await cn.OpenAsync();
            await using var cmd = new NpgsqlCommand("DROP SCHEMA IF EXISTS dbo CASCADE; DROP SCHEMA IF EXISTS aw_origen CASCADE", cn);
            await cmd.ExecuteNonQueryAsync();
        }
        finally { await LimpiarDestinoAsync(); }
    }

    internal static string DirectorioOrigenDemo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "tools", "aw-origen-demo", "schema.sql")))
                return Path.Combine(d.FullName, "tools", "aw-origen-demo");
        throw new InvalidOperationException("No se encontró tools/aw-origen-demo subiendo desde " + AppContext.BaseDirectory);
    }

    private async Task EjecutarAsync(string sql)
    {
        await using var cn = new NpgsqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync();
    }

    // ---------- origenes ----------
    private static AwClientesOptions OptsClientes(int lote = Lote) => new()
    {
        Origen = AwClientesOrigenTipo.Postgres, LecturaHabilitada = true, AplicacionHabilitada = true, TamanoLote = lote,
        SqlConnectTimeoutSeconds = 5, SqlQueryTimeoutSeconds = 15,
        MapeoMoneda = new(StringComparer.OrdinalIgnoreCase) { ["PESOSMX"] = "MXN", ["USD"] = "USD", ["Euro"] = "EUR" },
    };

    private static AwProductosOptions OptsProductos(int lote = Lote, params string[] excluidos) => new()
    {
        OrigenHabilitado = true, Origen = AwProductosOrigenTipo.Postgres, TamanoLote = lote,
        SqlConnectTimeoutSeconds = 5, SqlQueryTimeoutSeconds = 15, TiposExcluidos = excluidos,
    };

    private AwClientesPgOrigen OrigenClientes(AwClientesOptions? o = null, string? cs = null) =>
        new(new AwOrigenPg.Fabrica(cs ?? _cs), Options.Create(o ?? OptsClientes()), NullLogger<AwClientesPgOrigen>.Instance);

    private AwProductosPgOrigen OrigenProductos(AwProductosOptions? o = null, string? cs = null) =>
        new(new AwOrigenPg.Fabrica(cs ?? _cs), Options.Create(o ?? OptsProductos()), NullLogger<AwProductosPgOrigen>.Instance);

    // ---------- plomería (patrón de las pruebas de sandbox, con origen Postgres) ----------
    private async Task<T> ConDb<T>(Func<CompartidoDbContext, Task<T>> f)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await f(scope.ServiceProvider.GetRequiredService<CompartidoDbContext>());
    }

    private async Task LimpiarDestinoAsync()
    {
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones
                .Where(e => e.Estado == AwClientesEjecucionEstado.Pendiente || e.Estado == AwClientesEjecucionEstado.EnCurso)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Estado, AwClientesEjecucionEstado.Cancelada));

        var refs = Enumerable.Range(1, 2000).Select(i => i.ToString()).ToList();
        await ConDb(async db =>
        {
            await db.Set<ClienteSincronizacionAw>().Where(r => refs.Contains(r.ReferenciaExterna)).ExecuteDeleteAsync();
            await db.Clientes.IgnoreQueryFilters()
                .Where(c => c.Origen == OrigenMaster.Aw && c.ReferenciaExterna != null && refs.Contains(c.ReferenciaExterna))
                .ExecuteDeleteAsync();
            var ids = await db.ProductosAw.IgnoreQueryFilters().Where(p => refs.Contains(p.ReferenciaExterna)).Select(p => p.Id).ToListAsync();
            await db.ProductosAwComponentes.Where(c => ids.Contains(c.ProductoAwId)).ExecuteDeleteAsync();
            await db.ProductosAwVariantes.Where(v => ids.Contains(v.ProductoAwId)).ExecuteDeleteAsync();
            await db.ProductosSincronizacionAw.Where(r => ids.Contains(r.ProductoAwId)).ExecuteDeleteAsync();
            await db.ProductosAw.IgnoreQueryFilters().Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync();
            return 0;
        });
    }

    private async Task<AwClientesEjecucion> BarridoClientes(IAwClientesOrigen origen, AwClientesOptions opts)
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var sync = new AwClientesSincronizador(sp.GetRequiredService<IntegracionesAwDbContext>(),
            sp.GetRequiredService<CompartidoDbContext>(), sp.GetRequiredService<AplicarClienteAwService>(),
            new ServiceCollection().AddSingleton(origen).BuildServiceProvider(), Options.Create(opts), TimeProvider.System,
            NullLogger<AwClientesSincronizador>.Instance);
        var id = await sync.IniciarBarridoAsync("test-origen-pg", CancellationToken.None);
        await sync.EjecutarAsync(id, CancellationToken.None);
        return await sp.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones
            .AsNoTracking().Include(e => e.ErroresPorReferencia).SingleAsync(e => e.Id == id);
    }

    private async Task<AwProductosResumen> BarridoProductos(IAwProductosOrigen origen, AwProductosOptions opts) =>
        await ConDb(db => new AwProductosSincronizador(db, new AplicarProductoAwService(db),
            new ServiceCollection().AddSingleton(origen).BuildServiceProvider(), Options.Create(opts),
            TimeProvider.System, NullLogger<AwProductosSincronizador>.Instance).SincronizarBarridoAsync(default));

    // ---------- lectura de clientes ----------
    [Fact]
    public async Task Clientes_pagina_por_cursor_y_trae_las_coincidencias_del_catalogo()
    {
        var o = OrigenClientes();
        var p1 = await o.LeerPaginaAsync(null, Lote, default);
        var p2 = await o.LeerPaginaAsync(p1.SiguienteCursor, Lote, default);
        var p3 = await o.LeerPaginaAsync(p2.SiguienteCursor, Lote, default);

        Assert.Equal((Lote, "25", Lote, "50"), (p1.Filas.Count, p1.SiguienteCursor, p2.Filas.Count, p2.SiguienteCursor));
        Assert.Equal((Clientes - 2 * Lote, (string?)null), (p3.Filas.Count, p3.SiguienteCursor)); // página corta = fin
        var todas = p1.Filas.Concat(p2.Filas).Concat(p3.Filas).ToList();
        Assert.Equal(Enumerable.Range(1, Clientes), todas.Select(f => f.Id));

        // Un lote exacto devuelve cursor; la página siguiente viene vacía (misma regla que SQL Server).
        var exacta = await o.LeerPaginaAsync(null, Clientes, default);
        Assert.Equal(Clientes.ToString(), exacta.SiguienteCursor);
        Assert.Empty((await o.LeerPaginaAsync(exacta.SiguienteCursor, Clientes, default)).Filas);

        // Condición: 1 coincidencia exacta; sin coincidencia (minúscula / no existe en el catálogo) = vacío.
        Assert.Equal(new AwCondicionCoincidencia(6, 30), Assert.Single(todas.Single(f => f.Id == 19).CondicionCoincidencias));
        Assert.Equal(new AwCondicionCoincidencia(1, 0), Assert.Single(todas.Single(f => f.Id == 10).CondicionCoincidencias));
        Assert.Empty(todas.Single(f => f.Id == 53).CondicionCoincidencias);
        Assert.Empty(todas.Single(f => f.Id == 47).CondicionCoincidencias);
        Assert.Equal(new AwCondicionCoincidencia(0, null), Assert.Single(todas.Single(f => f.Id == 11).CondicionCoincidencias)); // '<indf>': BRUTTOTAGE nulo != 0

        // Columnas de la fila.
        var c10 = todas.Single(f => f.Id == 10);
        Assert.Equal(("CLIENTE DEMO 010", "PESOSMX", "DEMO010", 1, 1), (c10.Name1, c10.Waehrung, c10.UstId, c10.Mandant, c10.KzGesperrt));
        Assert.Equal(new DateOnly(2020, 1, 11), c10.Datum);
        Assert.Null(c10.TransactionTime);
        Assert.Equal(new DateTime(2026, 1, 1, 10, 0, 0), todas.Single(f => f.Id == 20).TransactionTime);
    }

    [Fact]
    public async Task Clientes_lee_por_referencia_y_rechaza_referencias_invalidas()
    {
        var o = OrigenClientes();
        Assert.Equal("CLIENTE DEMO 019", (await o.LeerPorReferenciaAsync("19", default))!.Name1);
        Assert.Null(await o.LeerPorReferenciaAsync("9999", default));
        Assert.Null(await o.LeerPorReferenciaAsync("abc", default));
        await Assert.ThrowsAsync<ArgumentException>(() => o.LeerPaginaAsync("x", Lote, default));
    }

    // ---------- lectura de productos ----------
    [Fact]
    public async Task Productos_pagina_por_cursor_con_variantes_y_componentes()
    {
        var o = OrigenProductos();
        var p1 = await o.LeerPaginaAsync(null, Lote, default);
        var p2 = await o.LeerPaginaAsync(p1.SiguienteCursor, Lote, default);

        Assert.Equal((Lote, "25", Productos - Lote, (string?)null), (p1.Filas.Count, p1.SiguienteCursor, p2.Filas.Count, p2.SiguienteCursor));
        var todos = p1.Filas.Concat(p2.Filas).ToList();
        Assert.Equal(Enumerable.Range(1, Productos).Select(i => i.ToString()), todos.Select(f => f.ProductoRef!)); // orden numérico

        var lam = todos.Single(f => f.ProductoRef == "25");
        Assert.Equal(("VIDRIO LAMINADO 3+3 PVB 0.76MM CLARO", "m²", false, "VLA", "Laminado"), (lam.Descripcion, lam.UnidadMedida, lam.Baja, lam.Tipo, lam.Grupo));
        Assert.Equal(["2440x3660", "1830x2440"], lam.Variantes.Select(v => v.ClaveVariante));
        Assert.Equal(new AwProductoOrigenVariante("2440x3660", 3660m, 2440m, 6.76m, "3+0.76+3"), lam.Variantes[0]);
        Assert.Equal([1, 2, 3], lam.Componentes!.Select(c => c.Orden));
        Assert.All(lam.Componentes!, c => Assert.Null(c.PadreOrden));
        Assert.Equal(new AwProductoOrigenComponente(2, 1, null, "41", "PVB 0.76MM", "Relleno", 0.76m), lam.Componentes![1]);

        // Nulo != 0: la variante BASE de un templado no informa alto ni ancho.
        var vte = todos.Single(f => f.ProductoRef == "13").Variantes.Single();
        Assert.Equal(("BASE", (decimal?)null, (decimal?)null, (decimal?)3m), (vte.ClaveVariante, vte.AltoMm, vte.AnchoMm, vte.EspesorMm));
        Assert.True(todos.Single(f => f.ProductoRef == "45").Baja);
        Assert.Empty(todos.Single(f => f.ProductoRef == "1").Componentes!);
    }

    [Fact]
    public async Task Productos_aplica_TiposExcluidos_solo_a_la_pagina_y_lee_por_referencia()
    {
        var o = OrigenProductos(OptsProductos(100, "VTE", "VLA"));
        var pagina = await o.LeerPaginaAsync(null, 100, default);
        Assert.Equal(Productos - 12 - 8, pagina.Filas.Count);
        Assert.DoesNotContain(pagina.Filas, f => f.Tipo is "VTE" or "VLA");

        Assert.Equal("VLA", (await o.LeerPorReferenciaAsync("25", default))!.Tipo); // por referencia no filtra, como en A+W
        Assert.Null(await o.LeerPorReferenciaAsync("0", default));
        Assert.Null(await o.LeerPorReferenciaAsync("9999", default));
        Assert.Null(await o.LeerPorReferenciaAsync("abc", default));
    }

    // ---------- el flujo completo: sincronizadores reales sobre el origen Postgres ----------
    [Fact]
    public async Task Barrido_de_clientes_crea_mapea_y_es_idempotente()
    {
        var e = await BarridoClientes(OrigenClientes(), OptsClientes());

        // 60 leídos; el 58 trae NAME1 vacío -> fila_invalida (único error).
        Assert.Equal(AwClientesEjecucionEstado.Parcial, e.Estado);
        Assert.Equal((Clientes, Clientes - 1, 1), (e.Leidos, e.Creados, e.Errores));
        Assert.Equal(("58", "fila_invalida"), (Assert.Single(e.ErroresPorReferencia).Referencia, e.ErroresPorReferencia.Single().Codigo));

        var (cs, regs) = await ConDb(async db =>
        {
            var c = await db.Clientes.AsNoTracking().Where(x => x.Origen == OrigenMaster.Aw && x.ReferenciaExterna != null).ToListAsync();
            var r = await db.Set<ClienteSincronizacionAw>().AsNoTracking().ToListAsync();
            return (c.Where(x => int.TryParse(x.ReferenciaExterna, out var n) && n <= 2000).ToDictionary(x => x.ReferenciaExterna!),
                    r.Where(x => int.TryParse(x.ReferenciaExterna, out var n) && n <= 2000).ToDictionary(x => x.ReferenciaExterna));
        });
        Assert.Equal(Clientes - 1, cs.Count);
        Assert.Equal(cs.Count, regs.Count);
        Assert.DoesNotContain("58", cs.Keys);
        Assert.All(cs.Values, c => Assert.Equal(EstatusCatalogo.Activo, c.Estatus));

        var (c10, r10) = (cs["10"], regs["10"]);
        Assert.Equal(("AW-10", "CLIENTE DEMO 010", "MXN", "5500000010"), (c10.Clave, c10.RazonSocial, c10.MonedaDefault, c10.Telefono));
        Assert.Equal(("CONTADO", 1, 0, "PESOSMX", "MXN"), (r10.CondicionCodigoOrigen, r10.CondicionNumeroOrigen, r10.DiasNominalesOrigen, r10.MonedaCodigoOrigen, r10.MonedaNormalizada));
        Assert.Equal("USD", cs["7"].MonedaDefault);
        Assert.Equal("EUR", cs["44"].MonedaDefault);
        Assert.Equal(("30 DIAS", 6, 30), (regs["19"].CondicionCodigoOrigen, regs["19"].CondicionNumeroOrigen, regs["19"].DiasNominalesOrigen));
        Assert.Null(regs["53"].CondicionNumeroOrigen); // 'contado' (minúscula) no coincide con el catálogo
        Assert.Null(c10.Rfc); // nunca se deriva RFC de UST_ID/STEUERNUMMER
        Assert.Equal("DEMO010", r10.CandidatoFiscalUstId);

        // Segundo barrido: idempotente. Un cambio en el origen se aplica en el siguiente barrido.
        var e2 = await BarridoClientes(OrigenClientes(), OptsClientes());
        Assert.Equal((Clientes, 0, 0, Clientes - 1), (e2.Leidos, e2.Creados, e2.Actualizados, e2.SinCambios));

        await EjecutarAsync("UPDATE aw_origen.ku_kunden SET name1 = 'CLIENTE EDITADO EN ORIGEN' WHERE id = 10");
        var e3 = await BarridoClientes(OrigenClientes(), OptsClientes());
        Assert.Equal((1, Clientes - 2), (e3.Actualizados, e3.SinCambios));
        // El cambio de origen queda en "Datos de origen A+W"; la razón social local no se pisa (doc 05).
        var (razon, origenNombre) = await ConDb(async db => (
            await db.Clientes.AsNoTracking().Where(x => x.ReferenciaExterna == "10" && x.Origen == OrigenMaster.Aw).Select(x => x.RazonSocial).SingleAsync(),
            await db.Set<ClienteSincronizacionAw>().AsNoTracking().Where(x => x.ReferenciaExterna == "10").Select(x => x.NombreComercialOrigen).SingleAsync()));
        Assert.Equal(("CLIENTE DEMO 010", "CLIENTE EDITADO EN ORIGEN"), (razon, origenNombre));
    }

    [Fact]
    public async Task Barrido_de_productos_crea_variantes_y_componentes_y_es_idempotente()
    {
        var r = await BarridoProductos(OrigenProductos(), OptsProductos());

        // 47 leídos: 46 sin descripción = error de fila; 47 con unidad sin equivalencia = pendiente (no se crea).
        Assert.Equal((Productos, Productos - 2, 1, 1), (r.Leidos, r.Creados, r.Pendientes, r.Errores));
        Assert.Equal(["46", "47"], r.ErroresPorReferencia.Select(x => x.Referencia).Order());
        Assert.Contains(r.ErroresPorReferencia, x => x.Referencia == "47" && x.Codigo == "UNIDAD_SIN_EQUIVALENCIA");

        var ps = await ConDb(async db =>
            (await db.ProductosAw.AsNoTracking().Include(p => p.Variantes).Include(p => p.Componentes).ToListAsync())
            .Where(p => int.TryParse(p.ReferenciaExterna, out var n) && n <= 2000).ToDictionary(p => p.ReferenciaExterna));
        Assert.Equal(Productos - 2, ps.Count);
        Assert.DoesNotContain("46", ps.Keys);
        Assert.DoesNotContain("47", ps.Keys);

        // Unidades normalizadas (m² -> M2, m lin. -> M) y datos de las variantes / el árbol.
        Assert.Equal("M2", ps["1"].UnidadMedida);
        Assert.Equal("M", ps["43"].UnidadMedida);
        var lam = ps["25"];
        Assert.Equal("VIDRIO LAMINADO 3+3 PVB 0.76MM CLARO", lam.Descripcion);
        Assert.Equal(["1830x2440", "2440x3660"], lam.Variantes.Select(v => v.ClaveVariante).Order());
        var v = lam.Variantes.Single(x => x.ClaveVariante == "2440x3660");
        Assert.Equal((3660m, 2440m, 6.76m, "3+0.76+3"), (v.AltoMm, v.AnchoMm, v.EspesorMm, v.Composicion));
        Assert.Equal(3, lam.Componentes.Count);
        var vte = ps["13"].Variantes.Single();
        Assert.Null(vte.AltoMm); // nulo != 0
        Assert.Null(vte.AnchoMm);
        Assert.Equal(3m, vte.EspesorMm);
        Assert.Equal(EstatusCatalogo.Inactivo, ps["45"].Estatus); // baja explícita en el origen
        Assert.Equal(EstatusCatalogo.Activo, ps["1"].Estatus);

        // Segundo barrido: idempotente. Un cambio en el origen se aplica en el siguiente barrido.
        var r2 = await BarridoProductos(OrigenProductos(), OptsProductos());
        Assert.Equal((Productos, 0, 0, Productos - 2), (r2.Leidos, r2.Creados, r2.Actualizados, r2.SinCambios));

        await EjecutarAsync("UPDATE aw_origen.erp_articulo SET descripcion = 'VIDRIO FLOAT CLARO 3MM EDITADO' WHERE producto_ref = 1");
        var r3 = await BarridoProductos(OrigenProductos(), OptsProductos());
        Assert.Equal((1, Productos - 3), (r3.Actualizados, r3.SinCambios));
    }

    // ---------- fallas: se clasifican igual que con SQL Server ----------
    [Fact]
    public async Task Fallas_de_conexion_se_clasifican_como_AwReaderException()
    {
        var b = new NpgsqlConnectionStringBuilder(_cs) { Password = "clave-erronea-QX7", Pooling = false };
        var auth = await Assert.ThrowsAsync<AwReaderException>(() => OrigenClientes(cs: b.ConnectionString).LeerPaginaAsync(null, Lote, default));
        Assert.Equal(("auth", false), (auth.Kind, auth.IsTransient));

        b = new NpgsqlConnectionStringBuilder(_cs) { Port = 1, Pooling = false };
        var caida = await Assert.ThrowsAsync<AwReaderException>(() => OrigenProductos(cs: b.ConnectionString).LeerPaginaAsync(null, Lote, default));
        Assert.True(caida.IsTransient && caida.Kind is "connection" or "connect_timeout", $"kind={caida.Kind}");

        // Sin las tablas (BD sin sembrar) el error también sale clasificado, no como excepción cruda de Npgsql.
        await EjecutarAsync("ALTER TABLE aw_origen.erp_articulo RENAME TO erp_articulo_x");
        try
        {
            var sinTabla = await Assert.ThrowsAsync<AwReaderException>(() => OrigenProductos().LeerPaginaAsync(null, Lote, default));
            Assert.Equal("connection", sinTabla.Kind);
        }
        finally { await EjecutarAsync("ALTER TABLE aw_origen.erp_articulo_x RENAME TO erp_articulo"); }
    }
}
