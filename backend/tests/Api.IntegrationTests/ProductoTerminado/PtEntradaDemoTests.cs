using System.Data.Common;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Application.Origen;
using Millet.Integraciones.Aw.Application.PiezasPt;
using Millet.Integraciones.Aw.Infrastructure.PiezasPt;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;
using Xunit.Abstractions;

namespace Millet.Api.IntegrationTests.ProductoTerminado;

/// <summary>
/// Entrada de piezas de PT de punta a punta: tablas del origen Demo (PostgreSQL) + worker real + módulo PT + outbox.
/// Todo con números <c>PT-INT-ED-&lt;guid&gt;</c>; al terminar cada prueba se borra solo lo creado por ella.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class PtEntradaDemoTests(WebApplicationFactory<Program> factory, ITestOutputHelper salida)
{
    private const string Prefijo = "PT-INT-ED-";
    private const string ClaveA = "PTINT-A";
    private const string ClaveB = "PTINT-B";
    private const string EventoPieza = "producto_terminado.pieza.ingresada.v1";
    private readonly PtDatos _d = new(factory);

    // ---------- infraestructura ----------

    private sealed class OrigenFijo(string origen) : IAwOrigenActivo
    {
        public Task<AwOrigenEstado> LeerAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AwOrigenEstado(origen, true, true, "Real", "Real", null, null, 1));
    }

    private sealed class FalloOutbox : DbCommandInterceptor
    {
        public volatile bool Activo;
        private void Revisar(DbCommand c)
        {
            if (Activo && c.CommandText.Contains("integration_events_outbox", StringComparison.Ordinal))
                throw new InvalidOperationException("Fallo simulado al guardar el outbox.");
        }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) { Revisar(command); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { Revisar(command); return ValueTask.FromResult(result); }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) { Revisar(command); return result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { Revisar(command); return ValueTask.FromResult(result); }
    }

    private static string Schema()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta != null)
        {
            var archivo = Path.Combine(carpeta.FullName, "tools", "aw-origen-demo", "schema.sql");
            if (File.Exists(archivo)) return File.ReadAllText(archivo);
            carpeta = carpeta.Parent;
        }
        throw new FileNotFoundException("No se encontró el esquema Demo de A+W.");
    }

    private string CadenaErp()
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ProductoTerminadoDbContext>().Database.GetConnectionString()!;
    }

    private async Task<int> Sql(string sql, params NpgsqlParameter[] p)
    {
        await using var c = new NpgsqlConnection(CadenaErp()); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c); cmd.Parameters.AddRange(p); return await cmd.ExecuteNonQueryAsync();
    }

    private async Task<long> Cuenta(string sql, string like)
    {
        await using var c = new NpgsqlConnection(CadenaErp()); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c); cmd.Parameters.AddWithValue("p", like);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<List<T>> Filas<T>(string sql, string like, Func<DbDataReader, T> mapa)
    {
        await using var c = new NpgsqlConnection(CadenaErp()); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c); cmd.Parameters.AddWithValue("p", like);
        await using var r = await cmd.ExecuteReaderAsync(); var l = new List<T>();
        while (await r.ReadAsync()) l.Add(mapa(r));
        return l;
    }

    private Task<long> Piezas(string n) => Cuenta("SELECT count(*) FROM producto_terminado.piezas z JOIN producto_terminado.pedidos_pt p ON p.id = z.pedido_pt_id WHERE p.numero_pedido LIKE @p || '%'", n);
    private Task<long> Movimientos(string n) => Cuenta("SELECT count(*) FROM producto_terminado.movimientos_pieza m JOIN producto_terminado.piezas z ON z.id = m.pieza_id JOIN producto_terminado.pedidos_pt p ON p.id = z.pedido_pt_id WHERE p.numero_pedido LIKE @p || '%'", n);
    private Task<long> Eventos(string n) => Cuenta($"SELECT count(*) FROM producto_terminado.integration_events_outbox WHERE event_type = '{EventoPieza}' AND payload::text LIKE '%' || @p || '%'", n);
    private Task<long> Escaneos(string n) => Cuenta("SELECT count(*) FROM producto_terminado.escaneos WHERE numero_pedido LIKE @p || '%'", n);
    private Task<long> EscaneosConResultado(string n, string resultado) => Cuenta($"SELECT count(*) FROM producto_terminado.escaneos WHERE resultado = '{resultado}' AND numero_pedido LIKE @p || '%'", n);
    private Task<List<(string Resultado, string? Motivo, int Intentos)>> Resultados(string n) =>
        Filas("SELECT resultado, motivo, intentos FROM producto_terminado.escaneos WHERE numero_pedido LIKE @p || '%' ORDER BY registrado_at, etiqueta", n,
            r => (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetInt32(2)));

    private static DateTime Ahora(int ms = 0)
    {
        var t = DateTime.UtcNow.AddMinutes(-2).AddMilliseconds(ms);
        return new DateTime(t.Ticks - t.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Unspecified);
    }

    private static string Numero() => Prefijo + Guid.NewGuid().ToString("N");

    private Task<int> Escaneo(string pedido, string etiqueta, int pieza, DateTime tiempo, string? clave = ClaveA, int estacion = 2821) => Sql("""
        INSERT INTO dbo.pool_pos (auftnr,pos,u_pos,ae_intpos,menge) VALUES (@a,1,0,1,100) ON CONFLICT DO NOTHING;
        INSERT INTO dbo.or_avbereich (auftnr,avbereich) SELECT @a,@c WHERE @c IS NOT NULL ON CONFLICT DO NOTHING;
        INSERT INTO dbo.awbar_bearbeit (etikettnr,bearb_zeit,auftnr,pos,u_pos,teile_nr,fertig,esnr_ist) VALUES (@e,@t,@a,1,0,@n,1,@s);
        """, new("a", pedido), new("c", (object?)clave ?? DBNull.Value) { NpgsqlDbType = NpgsqlDbType.Varchar }, new("e", etiqueta),
        new("t", NpgsqlDbType.Timestamp) { Value = tiempo }, new("n", pieza.ToString()), new("s", estacion));

    private sealed class Contexto(PtEntradaDemoTests t, Guid a, Guid b, string? claveA, string? claveB) : IAsyncDisposable
    {
        public Guid A { get; } = a;
        public Guid B { get; } = b;
        public async ValueTask DisposeAsync()
        {
            await t.LimpiarAsync();
            await t.Sql("UPDATE compartido.sucursales SET clave_aw=@c WHERE id=@i", new("c", (object?)claveA ?? DBNull.Value) { NpgsqlDbType = NpgsqlDbType.Varchar }, new("i", A));
            await t.Sql("UPDATE compartido.sucursales SET clave_aw=@c WHERE id=@i", new("c", (object?)claveB ?? DBNull.Value) { NpgsqlDbType = NpgsqlDbType.Varchar }, new("i", B));
        }
    }

    private async Task LimpiarAsync()
    {
        const string like = Prefijo + "%";
        var p = () => new NpgsqlParameter("l", like);
        await Sql("DELETE FROM producto_terminado.integration_events_outbox WHERE payload::text LIKE '%" + Prefijo + "%'");
        await Sql("DELETE FROM producto_terminado.movimientos_pieza WHERE pieza_id IN (SELECT id FROM producto_terminado.piezas WHERE etiqueta LIKE @l)", p());
        await Sql("DELETE FROM producto_terminado.piezas WHERE etiqueta LIKE @l", p());
        await Sql("DELETE FROM producto_terminado.escaneos WHERE numero_pedido LIKE @l", p());
        await Sql("DELETE FROM producto_terminado.pedido_pt_posiciones WHERE pedido_pt_id IN (SELECT id FROM producto_terminado.pedidos_pt WHERE numero_pedido LIKE @l)", p());
        await Sql("DELETE FROM producto_terminado.pedidos_pt WHERE numero_pedido LIKE @l", p());
        foreach (var t in new[] { "awbar_bearbeit", "pool_pos", "pool_bearbeit", "or_avbereich" })
            await Sql($"DELETE FROM dbo.{t} WHERE auftnr LIKE @l", p());
    }

    private async Task<Contexto> PrepararAsync()
    {
        await Sql(Schema());
        var (a, b) = await _d.SucursalesAsync();
        await using var c = new NpgsqlConnection(CadenaErp()); await c.OpenAsync();
        async Task<string?> Leer(Guid id)
        {
            await using var cmd = new NpgsqlCommand("SELECT clave_aw FROM compartido.sucursales WHERE id=@i", c); cmd.Parameters.AddWithValue("i", id);
            var v = await cmd.ExecuteScalarAsync(); return v is DBNull ? null : (string?)v;
        }
        var ctx = new Contexto(this, a, b, await Leer(a), await Leer(b));
        await Sql("UPDATE compartido.sucursales SET clave_aw=@c WHERE id=@i", new("c", ClaveA), new("i", a));
        await Sql("UPDATE compartido.sucursales SET clave_aw=@c WHERE id=@i", new("c", ClaveB), new("i", b));
        await LimpiarAsync();
        return ctx;
    }

    private WebApplicationFactory<Program> Host(string origen = "Demo", Action<IServiceCollection>? extra = null)
    {
        var cs = CadenaErp();
        return factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton<IAwOrigenActivo>(new OrigenFijo(origen));
            s.AddKeyedScoped<IPiezasPtOrigenReader>("Demo", (_, _) => new PiezasPtOrigenAdapter(() => new NpgsqlConnection(cs), true));
            extra?.Invoke(s);
        }));
    }

    private static Task<int> Ciclo(WebApplicationFactory<Program> f, bool i134 = false)
    {
        var opts = new PiezasPtOptions { EntradaHabilitada = true, EmpresaId = PtDatos.Empresa, EstacionesPt = ["2821-2840"], ContratoI134Firmado = i134 };
        var w = new PiezasPtEntradaWorker(f.Services.GetRequiredService<IServiceScopeFactory>(), Options.Create(opts), NullLogger<PiezasPtEntradaWorker>.Instance);
        return w.CorrerUnaVezAsync(CancellationToken.None);
    }

    private static string Etq(string pedido, int i) => $"{pedido}-E{i}";

    // ---------- casos ----------

    [Fact]
    public async Task M25_Con_el_candado_de_la_empresa_tomado_por_otra_sesion_el_ciclo_sale_sin_leer_y_al_liberarlo_procesa()
    {
        await using var ctx = await PrepararAsync();
        var n = Numero();
        await _d.FacturacionAsync(n, ctx.A, 5);
        await Escaneo(n, Etq(n, 1), 1, Ahora());
        await using var f = Host();

        await using (var otra = new NpgsqlConnection(CadenaErp()))
        {
            await otra.OpenAsync();
            await using (var toma = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@k, 0))", otra))
            {
                toma.Parameters.AddWithValue("k", $"millet:pt:piezas:worker:{PtDatos.Empresa}");
                Assert.True((bool)(await toma.ExecuteScalarAsync())!);
            }
            Assert.Equal(0, await Ciclo(f));
            Assert.Equal(0, await Escaneos(n));
            Assert.Equal(0, await Piezas(n));
            // La conexión vuelve al pool con la sesión viva: se libera el candado de forma explícita.
            await using var libera = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@k, 0))", otra);
            libera.Parameters.AddWithValue("k", $"millet:pt:piezas:worker:{PtDatos.Empresa}");
            Assert.True((bool)(await libera.ExecuteScalarAsync())!);
        }

        Assert.Equal(1, await Ciclo(f));
        Assert.Equal(1, await Piezas(n));
        Assert.Equal(1, await Escaneos(n));
    }

    [Fact]
    public async Task M01_CA5_9_Mismo_escaneo_leido_dos_veces_da_una_pieza_un_movimiento_y_un_evento()
    {
        await using var ctx = await PrepararAsync();
        var n = Numero();
        await _d.FacturacionAsync(n, ctx.A, 5);
        await Escaneo(n, Etq(n, 1), 1, Ahora());
        await using var f = Host();
        await Ciclo(f);
        var antes = await Resultados(n);
        await Ciclo(f);
        Assert.Equal(1, await Piezas(n));
        Assert.Equal(1, await Movimientos(n));
        Assert.Equal(1, await Eventos(n));
        Assert.Equal(1, await Escaneos(n));
        Assert.Equal(antes, await Resultados(n));
        Assert.Equal("Alta", Assert.Single(antes).Resultado);
    }

    [Fact]
    public async Task M02_M03_M61_Dos_piezas_quedan_EnPT_en_su_sucursal_con_hora_de_A_W_y_eventos_sin_estatus()
    {
        await using var ctx = await PrepararAsync();
        var n = Numero();
        await _d.FacturacionAsync(n, ctx.A, 5);
        var t1 = Ahora(); var t2 = Ahora(50);
        await Escaneo(n, Etq(n, 1), 1, t1);
        await Escaneo(n, Etq(n, 2), 2, t2);
        await using var f = Host();
        await Ciclo(f);

        var piezas = await Filas("SELECT numero_pieza, sucursal_id, estado, escaneada_at FROM producto_terminado.piezas z JOIN producto_terminado.pedidos_pt p ON p.id = z.pedido_pt_id WHERE p.numero_pedido LIKE @p || '%' ORDER BY numero_pieza", n,
            r => (Num: r.GetInt32(0), Suc: r.GetGuid(1), Estado: r.GetString(2), At: r.GetFieldValue<DateTimeOffset>(3)));
        Assert.Equal(2, piezas.Count);
        Assert.All(piezas, x => { Assert.Equal(ctx.A, x.Suc); Assert.Equal("EnPT", x.Estado); });
        Assert.Equal(HoraAw.AUtc(t1).UtcDateTime, piezas[0].At.UtcDateTime);
        Assert.Equal(HoraAw.AUtc(t2).UtcDateTime, piezas[1].At.UtcDateTime);

        var cargas = await Filas($"SELECT payload::text FROM producto_terminado.integration_events_outbox WHERE event_type = '{EventoPieza}' AND payload::text LIKE '%' || @p || '%'", n, r => r.GetString(0));
        Assert.Equal(2, cargas.Count);
        foreach (var carga in cargas)
        {
            using var doc = JsonDocument.Parse(carga);
            var llaves = doc.RootElement.EnumerateObject().Select(x => x.Name.ToLowerInvariant()).ToHashSet();
            foreach (var esperada in new[] { "piezaid", "numeropedido", "posicion", "productoid", "sucursalid", "escaneadaat" })
                Assert.Contains(esperada, llaves);
            Assert.DoesNotContain(llaves, k => k.Contains("estatus") || k.Contains("status") || k.Contains("69") || k == "estado");
        }

        using var http = await _d.ClienteAsync([PermisosCanonicos.ProductoTerminadoPiezasVer, PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales]);
        var respuesta = await http.GetAsync($"/api/v1/producto-terminado/piezas?pedido={n}");
        respuesta.EnsureSuccessStatusCode();
        var texto = await respuesta.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(texto);
        Assert.Equal(2, json.RootElement.GetProperty("total").GetInt32());
        Assert.DoesNotContain("\"69\"", texto);
        Assert.DoesNotContain("estatus", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("status", texto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task M04_PT5_1_b_Pedido_ausente_se_pospone_y_al_crearlo_el_siguiente_ciclo_da_una_pieza()
    {
        await using var ctx = await PrepararAsync();
        var n = Numero();
        await Escaneo(n, Etq(n, 1), 1, Ahora());
        await using var f = Host();
        await Ciclo(f);
        var fila = Assert.Single(await Resultados(n));
        Assert.Equal("Pospuesto", fila.Resultado);
        Assert.Equal("PT_PEDIDO_INEXISTENTE", fila.Motivo);
        Assert.Equal(0, await Piezas(n));

        await _d.FacturacionAsync(n, ctx.A, 5);
        await Ciclo(f);
        Assert.Equal(1, await Piezas(n));
        Assert.Equal("Alta", Assert.Single(await Resultados(n)).Resultado);
    }

    [Fact]
    public async Task M05_PT5_1_c_Pieza_mayor_que_la_cantidad_de_la_posicion_es_Excepcion_sin_pieza_ni_evento()
    {
        await using var ctx = await PrepararAsync();
        var n = Numero();
        await _d.FacturacionAsync(n, ctx.A, 3);
        await Escaneo(n, Etq(n, 4), 4, Ahora());
        await using var f = Host();
        await Ciclo(f);
        var fila = Assert.Single(await Resultados(n));
        Assert.Equal("Excepcion", fila.Resultado);
        Assert.Equal("PT_PIEZA_EXCEDE_POSICION", fila.Motivo);
        Assert.Equal(0, await Piezas(n));
        Assert.Equal(0, await Eventos(n));
    }

    [Fact]
    public async Task M06_PT5_1_d_Sin_contrato_I13_4_no_hay_lector_Real_y_el_ciclo_no_escribe_nada_pero_Demo_funciona()
    {
        await using var ctx = await PrepararAsync();
        var n = Numero();
        await _d.FacturacionAsync(n, ctx.A, 5);
        await Escaneo(n, Etq(n, 1), 1, Ahora());

        await using (var real = Host("Real"))
        {
            using (var scope = real.Services.CreateScope())
                Assert.Null(scope.ServiceProvider.GetKeyedService<IPiezasPtOrigenReader>("Real"));
            var error = await Record.ExceptionAsync(() => Ciclo(real, i134: false));
            Assert.NotNull(error);
            Assert.Equal(0, await Escaneos(n));
            Assert.Equal(0, await Piezas(n));
        }

        await using var demo = Host("Demo");
        await Ciclo(demo);
        Assert.Equal(1, await Piezas(n));
    }

    [Fact]
    public async Task M08_Ocho_de_diez_piezas_dan_avance_8_de_10_con_faltante_2_y_el_reenvio_no_cambia_nada()
    {
        await using var ctx = await PrepararAsync();
        var n = Numero();
        await _d.FacturacionAsync(n, ctx.A, 10);
        for (var i = 1; i <= 8; i++) await Escaneo(n, Etq(n, i), i, Ahora(i));
        await using var f = Host();
        await Ciclo(f);
        // A+W reenvía la etiqueta 3 con otra hora.
        await Escaneo(n, Etq(n, 3), 3, Ahora(500));
        await Ciclo(f);

        Assert.Equal(8, await Piezas(n));
        Assert.Equal(1, await EscaneosConResultado(n, "Reposicion"));
        using var http = await _d.ClienteAsync([PermisosCanonicos.ProductoTerminadoPiezasVer, PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales]);
        var json = await http.GetFromJsonAsync<JsonElement>($"/api/v1/producto-terminado/piezas?pedido={n}&pageSize=200");
        Assert.Equal(8, json.GetProperty("total").GetInt32());
        var avance = Assert.Single(json.GetProperty("avance").EnumerateArray());
        Assert.Equal(10, avance.GetProperty("esperadas").GetInt32());
        Assert.Equal(8, avance.GetProperty("terminadas").GetInt32());
        Assert.Equal(2, avance.GetProperty("faltantes").GetInt32());
        Assert.Equal("Parcial", avance.GetProperty("avanceFisico").GetString());
    }

    [Fact]
    public async Task M19_Clave_de_sucursal_sin_resolver_deja_Excepcion_PT_SUCURSAL_SIN_CLAVE_AW()
    {
        await using var ctx = await PrepararAsync();
        var n = Numero();
        await _d.FacturacionAsync(n, ctx.A, 5);
        await Escaneo(n, Etq(n, 1), 1, Ahora(), clave: "PTINT-NO-EXISTE");
        await using var f = Host();
        await Ciclo(f);
        var fila = Assert.Single(await Resultados(n));
        Assert.Equal("Excepcion", fila.Resultado);
        Assert.Equal("PT_SUCURSAL_SIN_CLAVE_AW", fila.Motivo);
        Assert.Equal(0, await Piezas(n));
    }

    [Fact]
    public async Task M24_Si_falla_el_guardado_del_outbox_no_queda_pieza_movimiento_ni_escaneo_y_el_siguiente_ciclo_lo_procesa()
    {
        await using var ctx = await PrepararAsync();
        var n = Numero();
        await _d.FacturacionAsync(n, ctx.A, 5);
        await Escaneo(n, Etq(n, 1), 1, Ahora());
        var fallo = new FalloOutbox { Activo = true };
        await using var f = Host(extra: s => s.ConfigureDbContext<ProductoTerminadoDbContext>((_, b) => b.AddInterceptors(fallo)));
        await Ciclo(f);
        Assert.Equal(0, await Piezas(n));
        Assert.Equal(0, await Movimientos(n));
        Assert.Equal(0, await Escaneos(n));
        Assert.Equal(0, await Eventos(n));

        fallo.Activo = false;
        await Ciclo(f);
        Assert.Equal(1, await Piezas(n));
        Assert.Equal(1, await Movimientos(n));
        Assert.Equal(1, await Eventos(n));
        Assert.Equal(1, await Escaneos(n));
    }

    [Fact]
    public async Task M07_CA5_14_Dos_mil_doscientas_cincuenta_piezas_distintas_mas_doscientas_cincuenta_repeticiones_sin_perdida()
    {
        await using var ctx = await PrepararAsync();
        const int pedidos = 25, piezasPorPedido = 90, repetidas = 10;
        var raiz = Prefijo + "V" + Guid.NewGuid().ToString("N")[..12];
        var t0 = Ahora();
        for (var o = 0; o < pedidos; o++)
        {
            var n = $"{raiz}-{o:00}";
            await _d.FacturacionAsync(n, ctx.A, piezasPorPedido);
            await Sql("""
                INSERT INTO dbo.pool_pos (auftnr,pos,u_pos,ae_intpos,menge) VALUES (@a,1,0,1,90);
                INSERT INTO dbo.or_avbereich (auftnr,avbereich) VALUES (@a,@c);
                INSERT INTO dbo.awbar_bearbeit (etikettnr,bearb_zeit,auftnr,pos,u_pos,teile_nr,fertig,esnr_ist)
                SELECT @a || '-E' || g, @t + ((@o * @pp + g) * interval '1 millisecond'), @a, 1, 0, g::text, 1, 2821 FROM generate_series(1, @pp) g;
                INSERT INTO dbo.awbar_bearbeit (etikettnr,bearb_zeit,auftnr,pos,u_pos,teile_nr,fertig,esnr_ist)
                SELECT @a || '-E' || g, @t + ((@np + @o * @rep + g) * interval '1 millisecond'), @a, 1, 0, g::text, 1, 2821 FROM generate_series(1, @rep) g;
                """, new("a", n), new("c", ClaveA), new("t", NpgsqlDbType.Timestamp) { Value = t0 }, new("o", o), new("pp", piezasPorPedido),
                new("np", pedidos * piezasPorPedido), new("rep", repetidas));
        }

        await using var f = Host();
        var reloj = Stopwatch.StartNew();
        var ciclos = 0;
        while (ciclos < 5 && await Escaneos(raiz) < pedidos * (piezasPorPedido + repetidas)) { await Ciclo(f); ciclos++; }
        reloj.Stop();
        salida.WriteLine($"CA5.14: {pedidos * (piezasPorPedido + repetidas)} filas leídas en {ciclos} ciclo(s), {reloj.Elapsed.TotalSeconds:F1} s.");

        Assert.Equal(pedidos * piezasPorPedido, await Piezas(raiz));
        Assert.Equal(pedidos * piezasPorPedido, await Movimientos(raiz));
        Assert.Equal(pedidos * piezasPorPedido, await Eventos(raiz));
        Assert.Equal(pedidos * repetidas, await EscaneosConResultado(raiz, "Reposicion"));
        Assert.Equal(pedidos * (piezasPorPedido + repetidas), await Escaneos(raiz));
        Assert.Equal(pedidos * piezasPorPedido, await EscaneosConResultado(raiz, "Alta"));
    }
}
