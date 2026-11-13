using System.Net;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Seed;
using Millet.ComercioExterior.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.ComercioExterior.Application.Ports;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Application.Origen;
using Millet.Integraciones.Aw.Infrastructure.Exportacion;
using Millet.SharedKernel.Application;
using Npgsql;
using Xunit.Abstractions;

namespace Millet.Api.IntegrationTests.ComercioExterior;
/// <summary>Host EntraId, JWT real del API y filtro CE; origen Demo efímero creado por la prueba.</summary>
[Trait("Category", "ComercioExteriorOtr")]
public sealed class OtrHttpTests(ITestOutputHelper output)
{
    private const string Base = "/api/v1/comercio-exterior/otr";
    private const string Expedientes = "/api/v1/comercio-exterior/expedientes";
    private static readonly Guid Cliente = DemoSesionSeedHostedService.Id("DEMO-CLI-EXP-1");
    private static Task<ExpedientesHttpTests.Escenario> Escenario(Action<IServiceCollection>? configurar = null, bool lectorReal = false) => ExpedientesHttpTests.Escenario.CrearAsync(s =>
    {
        s.Configure<OtrOptions>(o => { o.EntradaHabilitada = true; o.EmpresaId = DemoSesionSeedHostedService.EmpresaId; o.MaxIntentos = 2; });
        s.AddSingleton<IAwOrigenActivo, OrigenDemo>();
        configurar?.Invoke(s);
        s.AddKeyedScoped<OtrTablasAdapter>("Demo", (sp, _) => new(() => new NpgsqlConnection(sp.GetRequiredService<ComercioExteriorDbContext>().Database.GetConnectionString()), true,
            lectorReal ? ct => OtrDependencyInjection.YaLeidoAsync(sp.GetRequiredService<ComercioExteriorDbContext>(), DemoSesionSeedHostedService.EmpresaId, ct) : null));
    });
    private sealed class OrigenDemo : IAwOrigenActivo
    {
        public Task<AwOrigenEstado> LeerAsync(CancellationToken cancellationToken) => Task.FromResult(new AwOrigenEstado("Demo", true, true, "Real", "Real", null, null, 1));
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
        throw new FileNotFoundException("No se encontró el esquema Demo de OTR.");
    }
    private static async Task Sql(ExpedientesHttpTests.Escenario e, string sql, params NpgsqlParameter[] parameters)
    {
        using var scope = e.Scope(); var db = scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>();
        await using var c = new NpgsqlConnection(db.Database.GetConnectionString()); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c); cmd.Parameters.AddRange(parameters); await cmd.ExecuteNonQueryAsync();
    }
    private static async Task Preparar(ExpedientesHttpTests.Escenario e)
    {
        await Sql(e, Schema());
        await Sql(e, "UPDATE compartido.clientes SET referencia_externa='DEMO-CLI-EXP-1' WHERE id=@cliente", new NpgsqlParameter("cliente", Cliente));
        await Sql(e, "UPDATE compartido.sucursales SET clave_aw='DEMO-OTR-MID' WHERE id=@id", new NpgsqlParameter("id", e.SucursalId));
        using var scope = e.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var p = PedidoFacturable.ImportarDesdeAw(DemoSesionSeedHostedService.EmpresaId, "DEMO-OTR-PED-CE02", e.SucursalId, Cliente, "DEMO cliente", 1, ComportamientoFiscal.ExportacionConCce, "USD", null, null, "DEMO · Pedido sintético para reglas CE-02", 1, "15");
        p.AgregarLinea(null, "DEMO vidrio", "30171700", "H87", 20, 100, 0, false, tasaIva: 0); p.RecalcularTotal(); db.PedidosFacturables.Add(p); await db.SaveChangesAsync();
        await Insertar(e, "DEMO-OTR-1", 1, 100, "DEMO-OTR-PED-CE02");
        await Insertar(e, "DEMO-OTR-2", 1, 50, "DEMO-PED-INEXISTENTE");
    }
    private static Task Insertar(ExpedientesHttpTests.Escenario e, string id, long version, decimal peso, string pedido, string operacion = "Alta") => Sql(e, """
        BEGIN;
        INSERT INTO dbo.aw_otr_envio (otr_envio_id,version,cliente_numero_aw,sucursal_clave_aw,cliente,direccion_entrega,numero_contenedor,ruta,bultos,peso_kg,fecha_carga_prevista,operacion)
        VALUES (@id,@v,@cliente,@sucursal,'DEMO cliente','DEMO entrega','DEMO-C','DEMO-R',2,@peso,'2026-10-20',@op);
        INSERT INTO dbo.aw_otr_envio_linea (otr_envio_id,version,numero_pedido,posicion,cantidad,unidad) VALUES (@id,@v,@pedido,1,10,'H87');
        COMMIT;
        """, new NpgsqlParameter("id", id), new("v", version), new NpgsqlParameter("cliente", "DEMO-CLI-EXP-1"), new("sucursal", "DEMO-OTR-MID"), new("peso", peso), new("op", operacion), new("pedido", pedido));
    private static async Task<int> Entrada(ExpedientesHttpTests.Escenario e)
    { using var scope = e.Scope(); using var w = ActivatorUtilities.CreateInstance<OtrEntradaWorker>(scope.ServiceProvider); return await w.CorrerUnaVezAsync(CancellationToken.None); }
    private static async Task<JsonElement> Json(HttpResponseMessage r)
    { using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return doc.RootElement.Clone(); }
    private static Task<HttpResponseMessage> Post(HttpClient c, string ruta, object? body, string etag)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, ruta); if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); req.Headers.TryAddWithoutValidation("If-Match", etag); return c.SendAsync(req);
    }
    [Fact]
    public async Task Revalidar_sucursal_reconoce_alias_sin_reingestar_y_exige_permiso_alcance_version_e_idempotencia()
    {
        await using var e = await Escenario(lectorReal: true); await Preparar(e);
        await Sql(e, "UPDATE compartido.sucursales SET clave_aw=NULL WHERE id=@id", new NpgsqlParameter("id", e.SucursalId));
        Assert.Equal(2, await Entrada(e));
        using var corporativo = await e.ClienteAsync(true, leerTodas: true, gestionarTodas: true);
        var antes = (await Json(await corporativo.GetAsync(Base))).EnumerateArray().Single(x => x.GetProperty("idOrigen").GetString() == "DEMO-OTR-1");
        var id = antes.GetProperty("id").GetGuid(); var etag = $"\"{antes.GetProperty("version").GetInt32()}\"";
        var ruta = $"{Base}/{id}/revalidar-sucursal";
        string hash, contenido;
        using (var scope = e.Scope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            var row = await scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>().OtrEnvios.SingleAsync(x => x.Id == id);
            hash = row.Hash; contenido = row.ContenidoJson;
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(corporativo, ruta, null, etag)).StatusCode);
        await Sql(e, "UPDATE compartido.sucursales SET clave_aw='CONKAL,DEMO-OTR-MID' WHERE id=@id", new NpgsqlParameter("id", e.SucursalId));
        Assert.Equal(0, await Entrada(e)); // Actualizar catálogo no relee la versión OTR.
        using var local = await e.ClienteAsync(true);
        using var lectura = await e.ClienteAsync(true, soloLeer: true, leerTodas: true, gestionarTodas: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(local, ruta, null, etag)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(lectura, ruta, null, etag)).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await Post(corporativo, ruta, null, "")).StatusCode);
        var clave = Guid.NewGuid().ToString();
        async Task<HttpResponseMessage> Repetir()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, ruta);
            req.Headers.Add("Idempotency-Key", clave); req.Headers.TryAddWithoutValidation("If-Match", etag);
            return await corporativo.SendAsync(req);
        }
        using var respuesta = await Repetir(); respuesta.EnsureSuccessStatusCode();
        var despues = await Json(respuesta);
        Assert.Equal(e.SucursalId, despues.GetProperty("sucursalId").GetGuid());
        Assert.Equal(JsonValueKind.Null, despues.GetProperty("motivoVinculacion").ValueKind);
        Assert.Equal(antes.GetProperty("versionAplicada").GetInt64(), despues.GetProperty("versionAplicada").GetInt64());
        Assert.Equal(antes.GetProperty("lineas")[0].GetProperty("id").GetGuid(), despues.GetProperty("lineas")[0].GetProperty("id").GetGuid());
        Assert.Contains("CE_OTR_POR_VINCULAR", despues.GetProperty("lineas")[0].GetProperty("motivo").GetString());
        using var repetida = await Repetir(); repetida.EnsureSuccessStatusCode();
        Assert.Equal(respuesta.Headers.ETag, repetida.Headers.ETag);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(corporativo, ruta, null, etag)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(corporativo, ruta, null, respuesta.Headers.ETag!.Tag)).StatusCode);
        using (var scope = e.Scope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>();
            var row = await db.OtrEnvios.Include(x => x.Lineas).Include(x => x.Conflictos).SingleAsync(x => x.Id == id);
            Assert.Equal(hash, row.Hash); Assert.Equal(contenido, row.ContenidoJson);
            Assert.Single(row.Lineas); Assert.Empty(row.Conflictos); Assert.Null(row.ExpedienteId); Assert.Null(row.ContenedorId);
            Assert.NotNull(row.UpdatedBy); Assert.True(row.Version > antes.GetProperty("version").GetInt32());
        }
        Assert.Equal(0, await Entrada(e));
        using var abierto = await Post(corporativo, $"{Base}/{id}/abrir-expediente", new { responsableUsuarioId = e.Responsable, moneda = "USD" }, respuesta.Headers.ETag!.Tag);
        abierto.EnsureSuccessStatusCode(); // La recuperación permite continuar con el flujo de vinculación real del servidor de pruebas.
    }
    [Theory]
    [InlineData("")]
    [InlineData("/bandeja")]
    public async Task Bandeja_incluye_vinculados_y_cancelados_respetando_sucursal_y_expediente(string consulta)
    {
        await using var e = await Escenario(); await Preparar(e);
        await Insertar(e, "DEMO-OTR-CANCELADO", 1, 60, "DEMO-OTR-PED-CE02", "Cancelacion");
        await Entrada(e);
        using var admin = await e.ClienteAsync(true);
        var inicial = await Json(await admin.GetAsync(Base));
        var envio = inicial.EnumerateArray().Single(x => x.GetProperty("idOrigen").GetString() == "DEMO-OTR-1");
        using var vincular = await Post(admin, $"{Base}/{envio.GetProperty("id").GetGuid()}/abrir-expediente", new { responsableUsuarioId = e.Responsable, moneda = "USD" }, $"\"{envio.GetProperty("version").GetInt32()}\"");
        vincular.EnsureSuccessStatusCode(); var expediente = (await Json(vincular)).GetProperty("expedienteId").GetGuid();
        var todos = await Json(await admin.GetAsync(Base + consulta));
        Assert.Equal(Enumerable.Range(1, 3), todos.EnumerateArray().Select(x => x.GetProperty("estado").GetInt32()).Order().ToArray());
        var detalle = await Json(await admin.GetAsync($"{Base}?expedienteId={expediente}"));
        Assert.Single(detalle.EnumerateArray()); Assert.Equal(2, detalle[0].GetProperty("estado").GetInt32());
        using var ajeno = await e.ClienteAsync(true, asociar: false);
        Assert.Empty((await Json(await ajeno.GetAsync(Base + consulta))).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await ajeno.GetAsync($"{Base}?expedienteId={expediente}")).StatusCode);
    }
    [Fact]
    public async Task Bandeja_lee_solo_cabeceras_con_44000_versiones_y_conserva_detalle_y_aislamiento()
    {
        var sql = new SqlOtr();
        await using var e = await Escenario(s => s.ConfigureDbContext<ComercioExteriorDbContext>((_, b) => b.AddInterceptors(sql)));
        await Preparar(e); await Entrada(e);
        await Insertar(e, "DEMO-OTR-1", 44001, 100, "DEMO-OTR-PED-CE02", "Modificacion"); await Entrada(e);
        // Histórico sintético local: las versiones anteriores no deben encarecer la bandeja.
        await Sql(e, """
            INSERT INTO comercio_exterior.otr_lineas
            SELECT (jsonb_populate_record(NULL::comercio_exterior.otr_lineas,
                to_jsonb(l) || jsonb_build_object('id', gen_random_uuid(), 'version_origen', n))).*
            FROM comercio_exterior.otr_lineas l
            JOIN comercio_exterior.otr_envios e ON e.id=l.otr_envio_id
            CROSS JOIN generate_series(2,44000) n
            WHERE e.id_origen='DEMO-OTR-1' AND l.version_origen=1;
            """);
        using (var scope = e.Scope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>();
            for (var i = 0; i < 387; i++)
            {
                var fila = new OtrEnvio(DemoSesionSeedHostedService.EmpresaId, $"DEMO-OTR-VOLUMEN-{i}");
                fila.Aplicar(1, new(Cliente, e.SucursalId, "DEMO cliente", "DEMO entrega", null,
                    new DatosContenedor(null, null, null, null, null, null, null, null), [new("DEMO-PED", 1, 1, "H87")]));
                db.OtrEnvios.Add(fila);
            }
            var ajeno = new OtrEnvio(Guid.NewGuid(), "DEMO-OTR-OTRA-EMPRESA");
            ajeno.Aplicar(1, new(Cliente, e.SucursalId, "DEMO otra empresa", "DEMO entrega", null,
                new DatosContenedor(null, null, null, null, null, null, null, null), [new("DEMO-PED", 1, 1, "H87")]));
            db.OtrEnvios.Add(ajeno); await db.SaveChangesAsync();
        }
        using var admin = await e.ClienteAsync(true); admin.Timeout = TimeSpan.FromMinutes(3);
        var reloj = Stopwatch.StartNew();
        using var completa = await admin.GetAsync(Base); completa.EnsureSuccessStatusCode();
        var detalleJson = await completa.Content.ReadAsStringAsync(); var detalleMs = reloj.Elapsed.TotalMilliseconds;
        using var documentoDetalle = JsonDocument.Parse(detalleJson);
        var detalles = documentoDetalle.RootElement;
        var detalle = detalles.EnumerateArray().Single(x => x.GetProperty("idOrigen").GetString() == "DEMO-OTR-1");
        Assert.Equal(44001, detalle.GetProperty("lineas").GetArrayLength());
        sql.Comandos.Clear(); reloj.Restart();
        using var respuesta = await admin.GetAsync(Base + "/bandeja"); respuesta.EnsureSuccessStatusCode();
        var resumenJson = await respuesta.Content.ReadAsStringAsync(); var resumenMs = reloj.Elapsed.TotalMilliseconds;
        var comandos = sql.Comandos.ToArray();
        Assert.Single(comandos, c => c.Contains("FROM comercio_exterior.otr_envios", StringComparison.Ordinal));
        Assert.DoesNotContain(comandos, c => c.Contains("otr_lineas", StringComparison.Ordinal) || c.Contains("otr_conflictos", StringComparison.Ordinal));
        using var resumen = JsonDocument.Parse(resumenJson);
        Assert.Equal(389, resumen.RootElement.GetArrayLength());
        foreach (var fila in resumen.RootElement.EnumerateArray())
        {
            var original = detalles.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == fila.GetProperty("id").GetGuid());
            foreach (var campo in new[] { "idOrigen", "version", "versionAplicada", "sucursalId", "estado", "motivoVinculacion" })
                Assert.Equal(original.GetProperty(campo).ToString(), fila.GetProperty(campo).ToString());
            foreach (var campo in new[] { "cliente", "direccionEntrega", "producto", "contenedor" })
                Assert.Equal(original.GetProperty("contenido").GetProperty(campo).ToString(), fila.GetProperty("contenido").GetProperty(campo).ToString());
            Assert.False(fila.TryGetProperty("lineas", out _)); Assert.False(fila.TryGetProperty("conflictos", out _));
            Assert.False(fila.GetProperty("contenido").TryGetProperty("lineas", out _));
        }
        Assert.True(resumenJson.Length < detalleJson.Length / 20);
        using var sinPermisos = await e.ClienteAsync(false);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermisos.GetAsync(Base + "/bandeja")).StatusCode);
        output.WriteLine($"OTR_BENCH envios=389 historico=44000 detalle_ms={detalleMs:F1} bandeja_ms={resumenMs:F1} detalle_chars={detalleJson.Length} bandeja_chars={resumenJson.Length}");
    }
    private sealed class SqlOtr : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Comandos { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Comandos.Enqueue(command.CommandText); return ValueTask.FromResult(result); }
    }
    [Fact]
    public async Task Lector_real_solo_lee_no_duplica_registra_nueva_version_y_no_crea_clientes()
    {
        await using var e = await Escenario(lectorReal: true); await Preparar(e);
        await Insertar(e, "DEMO-OTR-3", 1, 70, "DEMO-OTR-PED-CE02");
        await Sql(e, "UPDATE dbo.aw_otr_envio SET cliente_numero_aw='DEMO-DESCONOCIDO' WHERE otr_envio_id='DEMO-OTR-3'");
        Assert.Equal(3, await Entrada(e)); Assert.Equal(0, await Entrada(e));
        await Insertar(e, "DEMO-OTR-1", 2, 125, "DEMO-OTR-PED-CE02", "Modificacion");
        Assert.Equal(1, await Entrada(e)); Assert.Equal(0, await Entrada(e));
        using var scope = e.Scope(); using var b = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>();
        Assert.Equal(3, await db.OtrEnvios.CountAsync());
        Assert.Equal(2, (await db.OtrEnvios.SingleAsync(x => x.IdOrigen == "DEMO-OTR-1")).VersionAplicada);
        var sinCliente = await db.OtrEnvios.SingleAsync(x => x.IdOrigen == "DEMO-OTR-3");
        Assert.Equal(EstadoOtrEnvio.PorVincular, sinCliente.Estado); Assert.Equal("CE_OTR_CLIENTE_DESCONOCIDO", sinCliente.MotivoVinculacion);
        await using var c = new NpgsqlConnection(db.Database.GetConnectionString()); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM dbo.aw_otr_envio WHERE erp_resultado IS NOT NULL OR erp_motivo IS NOT NULL OR erp_procesada_at IS NOT NULL", c);
        Assert.Equal(0L, await cmd.ExecuteScalarAsync()); // A+W no recibe ninguna escritura del ERP.
    }
    [Fact]
    public async Task Entrada_y_booking_se_quedan_en_erp_y_el_hito_no_permite_omitir_el_despacho()
    {
        await using var e = await Escenario(); await Preparar(e); using var admin = await e.ClienteAsync(true);
        Assert.Equal(2, await Entrada(e));
        var bandeja = await Json(await admin.GetAsync(Base)); Assert.Equal(2, bandeja.GetArrayLength());
        var envio = bandeja.EnumerateArray().Single(x => x.GetProperty("idOrigen").GetString() == "DEMO-OTR-1"); var id = envio.GetProperty("id").GetGuid();
        var otro = bandeja.EnumerateArray().Single(x => x.GetProperty("idOrigen").GetString() == "DEMO-OTR-2");
        Assert.Equal(1, otro.GetProperty("estado").GetInt32()); Assert.Contains("CE_OTR_PEDIDO_INEXISTENTE", otro.GetProperty("lineas")[0].GetProperty("motivo").GetString());
        var abierto = await Post(admin, $"{Base}/{id}/abrir-expediente", new { responsableUsuarioId = e.Responsable, moneda = "USD" }, $"\"{envio.GetProperty("version").GetInt32()}\""); abierto.EnsureSuccessStatusCode();
        var vinculado = await Json(abierto); var expediente = vinculado.GetProperty("expedienteId").GetGuid(); var contenedor = vinculado.GetProperty("contenedorId").GetGuid();
        Assert.Equal(2, vinculado.GetProperty("lineas")[0].GetProperty("estado").GetInt32());
        using (var scope = e.Scope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>();
            var propuesta = await db.Partidas.SingleAsync(p => p.ExpedienteId == expediente);
            Assert.Equal(EstadoPartida.RequiereRevision, propuesta.Estado); Assert.Equal(Guid.Empty, propuesta.ConfirmadoPorId);
        }
        await Insertar(e, "DEMO-OTR-1", 2, 125, "DEMO-OTR-PED-CE02", "Modificacion"); Assert.Equal(1, await Entrada(e));
        var actual = (await Json(await admin.GetAsync($"{Base}?expedienteId={expediente}")))[0]; Assert.Equal(2, actual.GetProperty("versionAplicada").GetInt64());
        using (var scope = e.Scope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>(); Assert.Equal(125, (await db.Contenedores.SingleAsync(c => c.Id == contenedor)).PesoKg);
            Assert.Single(await db.Partidas.Where(p => p.ExpedienteId == expediente).ToArrayAsync());
        }
        await Sql(e, "UPDATE dbo.aw_otr_envio SET erp_resultado=NULL WHERE otr_envio_id='DEMO-OTR-1' AND version=2"); await Entrada(e);
        var repetido = (await Json(await admin.GetAsync($"{Base}?expedienteId={expediente}")))[0]; Assert.Equal(actual.GetProperty("hash").GetString(), repetido.GetProperty("hash").GetString()); Assert.Equal(actual.GetProperty("version").GetInt32(), repetido.GetProperty("version").GetInt32());
        var hoy = FechaContable.HoyLocal(e.Ahora);
        var evidenciaDespacho = await e.AdjuntoAsync(expediente, await e.TipoAsync("EXP_PEDIMENTO"));
        using (var scope = e.Scope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>(); var exp = await db.Expedientes.Include(x => x.Hitos).Include(x => x.Participantes).SingleAsync(x => x.Id == expediente);
            exp.AgregarParticipante(Guid.NewGuid(), "DEMO agente", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, null, null);
            foreach (var h in exp.Hitos.Where(h => h.Orden > 1 && h.Orden < 9).OrderBy(h => h.Orden)) exp.RegistrarHito(h.Clave, hoy, e.Responsable, evidenciaDespacho, null, [], [evidenciaDespacho], hoy);
            await db.SaveChangesAsync();
        }
        var detalle = await admin.GetAsync($"{Expedientes}/{expediente}"); detalle.EnsureSuccessStatusCode();
        using var autorizador = await e.ClienteAsync(true);
        var autorizada = await Post(autorizador, $"{Expedientes}/{expediente}/autorizar-salida", new { }, detalle.Headers.ETag!.Tag); autorizada.EnsureSuccessStatusCode();
        var embarcado = await Post(admin, $"{Expedientes}/{expediente}/hitos/embarcado/real", new { fechaReal = hoy, responsableUsuarioId = e.Responsable }, autorizada.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, embarcado.StatusCode);
        Assert.Equal("CE_EMBARQUE_PENDIENTE", (await Json(embarcado)).GetProperty("code").GetString());
        var contenedores = await admin.GetAsync($"{Expedientes}/{expediente}/contenedores");
        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"{Expedientes}/{expediente}/contenedores/{contenedor}") { Content = JsonContent.Create(new { numero = "DEMO-C", booking = "DEMO-BOOK", ruta = "DEMO-R", pesoKg = 125, bultos = 2 }) };
        patch.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); patch.Headers.TryAddWithoutValidation("If-Match", contenedores.Headers.ETag!.Tag); (await admin.SendAsync(patch)).EnsureSuccessStatusCode();
        using (var scope = e.Scope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            // A+W no lee lo que el ERP deja (Jorge, 1-nov): booking, embarque e hitos se quedan en el expediente.
            var db = scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>();
            Assert.Equal("DEMO-BOOK", (await db.Contenedores.SingleAsync(c => c.Id == contenedor)).Booking);
            Assert.Empty(await db.OtrSalidas.ToArrayAsync());
        }
        using (var ajeno = await e.ClienteAsync(true, asociar: false)) Assert.Empty((await Json(await ajeno.GetAsync(Base))).EnumerateArray());
    }
    private sealed class PedidosConError : IPedidosEmbarcablesReadPort
    {
        public Task<IReadOnlyList<PedidoEmbarcable>> BuscarPorNumeroAsync(Guid empresaId, IReadOnlyCollection<string> numeros, CancellationToken ct) =>
            numeros.Contains("DEMO-ERROR") ? throw new InvalidOperationException(new string('x', 600)) : Task.FromResult<IReadOnlyList<PedidoEmbarcable>>([]);
        public Task<IReadOnlyList<PedidoEmbarcable>> ListarAsync(Guid clienteId, Guid sucursalId, CancellationToken ct) => Task.FromResult<IReadOnlyList<PedidoEmbarcable>>([]);
        public Task<IReadOnlyList<PedidoEmbarcable>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<PedidoEmbarcable>>([]);
    }
    [Fact]
    public async Task Error_tecnico_se_reintenta_por_fila_sin_detener_el_lote_y_luego_se_rechaza()
    {
        await using var e = await Escenario(s => s.AddScoped<IPedidosEmbarcablesReadPort, PedidosConError>()); await Preparar(e);
        await Sql(e, "UPDATE dbo.aw_otr_envio_linea SET numero_pedido='DEMO-ERROR' WHERE otr_envio_id='DEMO-OTR-1'");
        Assert.Equal(1, await Entrada(e));
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>();
            var control = await db.OtrEntradasControl.SingleAsync(x => x.IdOrigen == "DEMO-OTR-1");
            Assert.Equal(1, control.Intentos); Assert.False(control.Rechazada); Assert.Equal(500, control.UltimoError!.Length); Assert.StartsWith("CE_OTR_ERROR_TECNICO", control.UltimoError);
            Assert.True(await db.OtrEnvios.AnyAsync(x => x.IdOrigen == "DEMO-OTR-2"));
            await using var c = new NpgsqlConnection(db.Database.GetConnectionString()); await c.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT erp_resultado FROM dbo.aw_otr_envio WHERE otr_envio_id='DEMO-OTR-1'", c); Assert.Equal("Error", await cmd.ExecuteScalarAsync());
        }
        await Entrada(e);
        using var final = e.Scope(); using var b = final.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var dbFinal = final.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>();
        var rechazo = await dbFinal.OtrEntradasControl.SingleAsync(x => x.IdOrigen == "DEMO-OTR-1"); Assert.Equal(2, rechazo.Intentos); Assert.True(rechazo.Rechazada);
        await using var conn = new NpgsqlConnection(dbFinal.Database.GetConnectionString()); await conn.OpenAsync();
        await using var comprobar = new NpgsqlCommand("SELECT erp_resultado FROM dbo.aw_otr_envio WHERE otr_envio_id='DEMO-OTR-1'", conn); Assert.Equal("Rechazada", await comprobar.ExecuteScalarAsync());
        // Diagnóstico 10446260: el rechazo se ve en la bandeja (pestaña Rechazados) con su motivo, sólo para la gestión de todas las sucursales.
        using var corporativo = await e.ClienteAsync(true, leerTodas: true, gestionarTodas: true);
        var rechazos = await Json(await corporativo.GetAsync(Base + "/rechazos"));
        var fila = rechazos.EnumerateArray().Single(x => x.GetProperty("idOrigen").GetString() == "DEMO-OTR-1");
        Assert.StartsWith("CE_OTR_ERROR_TECNICO", fila.GetProperty("motivo").GetString()); Assert.Equal(2, fila.GetProperty("intentos").GetInt32());
        using var local = await e.ClienteAsync(true);
        Assert.Empty((await Json(await local.GetAsync(Base + "/rechazos"))).EnumerateArray());
        using var sinPermisos = await e.ClienteAsync(false);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermisos.GetAsync(Base + "/rechazos")).StatusCode);
    }
    [Fact]
    public async Task Pedido_ambiguo_permanece_pendiente_antes_y_despues_de_vincular()
    {
        await using var e = await Escenario(); await Preparar(e);
        using (var scope = e.Scope())
        {
            using var b = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var duplicado = PedidoFacturable.ImportarDesdeAw(DemoSesionSeedHostedService.EmpresaId, "DEMO-OTR-PED-CE02", e.SucursalId, Cliente, "DEMO cliente", 1, ComportamientoFiscal.ExportacionConCce, "USD", null, null, "DEMO duplicado", 1, "15");
            duplicado.AgregarLinea(null, "DEMO vidrio", "30171700", "H87", 20, 100, 0, false, tasaIva: 0); duplicado.RecalcularTotal(); db.PedidosFacturables.Add(duplicado); await db.SaveChangesAsync();
        }
        await Entrada(e); using var admin = await e.ClienteAsync(true);
        var envio = (await Json(await admin.GetAsync(Base))).EnumerateArray().Single(x => x.GetProperty("idOrigen").GetString() == "DEMO-OTR-1");
        Assert.StartsWith("CE_OTR_PEDIDO_AMBIGUO", envio.GetProperty("lineas")[0].GetProperty("motivo").GetString());
        var abierto = await Post(admin, $"{Base}/{envio.GetProperty("id").GetGuid()}/abrir-expediente", new { responsableUsuarioId = e.Responsable, moneda = "USD" }, $"\"{envio.GetProperty("version").GetInt32()}\""); abierto.EnsureSuccessStatusCode();
        var linea = (await Json(abierto)).GetProperty("lineas")[0]; Assert.Equal(1, linea.GetProperty("estado").GetInt32()); Assert.StartsWith("CE_OTR_PEDIDO_AMBIGUO", linea.GetProperty("motivo").GetString());
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Referencias_aw_desconocidas_conservan_envio_por_vincular(bool cliente)
    {
        await using var e = await Escenario(); await Preparar(e);
        await Sql(e, cliente ? "UPDATE dbo.aw_otr_envio SET cliente_numero_aw='DEMO-DESCONOCIDO' WHERE otr_envio_id='DEMO-OTR-1'" : "UPDATE dbo.aw_otr_envio SET sucursal_clave_aw='DEMO-DESCONOCIDA' WHERE otr_envio_id='DEMO-OTR-1'");
        await Entrada(e);
        using var scope = e.Scope(); using var b = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var envio = await scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>().OtrEnvios.SingleAsync(x => x.IdOrigen == "DEMO-OTR-1");
        Assert.Equal(EstadoOtrEnvio.PorVincular, envio.Estado); Assert.Equal(cliente ? "CE_OTR_CLIENTE_DESCONOCIDO" : "CE_OTR_SUCURSAL_DESCONOCIDA", envio.MotivoVinculacion);
    }
    [Fact]
    public async Task Misma_version_con_otro_contenido_se_rechaza_sin_alterar_lo_aplicado()
    {
        await using var e = await Escenario(); await Preparar(e); await Entrada(e);
        await Sql(e, "UPDATE dbo.aw_otr_envio SET peso_kg=999,erp_resultado=NULL WHERE otr_envio_id='DEMO-OTR-1'");
        await Entrada(e);
        using var scope = e.Scope(); using var b = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>(); var envio = await db.OtrEnvios.SingleAsync(x => x.IdOrigen == "DEMO-OTR-1");
        Assert.Equal(100, envio.Contenido().Contenedor.PesoKg);
        await using var c = new NpgsqlConnection(db.Database.GetConnectionString()); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT erp_resultado,erp_motivo FROM dbo.aw_otr_envio WHERE otr_envio_id='DEMO-OTR-1'", c); await using var reader = await cmd.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        Assert.Equal("Rechazada", reader.GetString(0)); Assert.Equal("CE_OTR_VERSION_CONTENIDO_DISTINTO", reader.GetString(1));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Piezas_editadas_manualmente_generan_conflicto_y_decision_auditada(bool usarOtr)
    {
        await using var e = await Escenario(); await Preparar(e); await Entrada(e); using var admin = await e.ClienteAsync(true);
        var envio = (await Json(await admin.GetAsync(Base))).EnumerateArray().Single(x => x.GetProperty("idOrigen").GetString() == "DEMO-OTR-1");
        var id = envio.GetProperty("id").GetGuid();
        var abierto = await Post(admin, $"{Base}/{id}/abrir-expediente", new { responsableUsuarioId = e.Responsable, moneda = "USD" }, $"\"{envio.GetProperty("version").GetInt32()}\""); abierto.EnsureSuccessStatusCode();
        var vinculo = await Json(abierto); var exp = vinculo.GetProperty("expedienteId").GetGuid(); var contenedor = vinculo.GetProperty("contenedorId").GetGuid();
        var detalle = await admin.GetAsync($"{Expedientes}/{exp}/contenedores"); detalle.EnsureSuccessStatusCode();
        var partida = (await Json(detalle)).GetProperty("partidas")[0]; var pedido = partida.GetProperty("pedidoFacturableId").GetGuid();
        using var quitar = new HttpRequestMessage(HttpMethod.Delete, $"{Expedientes}/{exp}/partidas/{partida.GetProperty("id").GetGuid()}");
        quitar.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); quitar.Headers.TryAddWithoutValidation("If-Match", detalle.Headers.ETag!.Tag);
        var retirada = await admin.SendAsync(quitar); retirada.EnsureSuccessStatusCode();
        var manual = await Post(admin, $"{Expedientes}/{exp}/contenedores/{contenedor}/partidas", new { pedidoFacturableId = pedido, posicion = 1, cantidad = 6 }, retirada.Headers.ETag!.Tag); manual.EnsureSuccessStatusCode();
        await Insertar(e, "DEMO-OTR-1", 2, 125, "DEMO-OTR-PED-CE02", "Modificacion"); await Entrada(e);
        var recibido = (await Json(await admin.GetAsync($"{Base}?expedienteId={exp}")))[0];
        var conflicto = recibido.GetProperty("conflictos").EnumerateArray().Single(c => c.GetProperty("campo").GetString()!.StartsWith("Partida:", StringComparison.Ordinal));
        var vigente = await Json(await admin.GetAsync($"{Expedientes}/{exp}/contenedores"));
        Assert.Equal(6, vigente.GetProperty("partidas")[0].GetProperty("cantidad").GetDecimal()); Assert.Equal(2, vigente.GetProperty("partidas")[0].GetProperty("estado").GetInt32());
        var resuelto = await Post(admin, $"{Base}/{id}/conflictos/{conflicto.GetProperty("id").GetGuid()}/resolver", new { usarOtr, motivo = "DEMO decisión de piezas revisada" }, $"\"{recibido.GetProperty("version").GetInt32()}\""); resuelto.EnsureSuccessStatusCode();
        var decision = (await Json(resuelto)).GetProperty("conflictos").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == conflicto.GetProperty("id").GetGuid());
        Assert.Equal(usarOtr, decision.GetProperty("usarOtr").GetBoolean()); Assert.Equal(JsonValueKind.String, decision.GetProperty("resueltoPorId").ValueKind);
        var despues = (await Json(await admin.GetAsync($"{Expedientes}/{exp}/contenedores"))).GetProperty("partidas"); Assert.Equal(1, despues.GetArrayLength()); Assert.Equal(usarOtr ? 10 : 6, despues[0].GetProperty("cantidad").GetDecimal());
    }
    [Fact]
    public async Task Permiso_otro_expediente_y_sucursal_se_validan_en_http_y_filtro_ce()
    {
        await using var e = await Escenario(); await Preparar(e); await Entrada(e);
        using var admin = await e.ClienteAsync(true); var envio = (await Json(await admin.GetAsync(Base)))[0]; var id = envio.GetProperty("id").GetGuid(); var etag = $"\"{envio.GetProperty("version").GetInt32()}\"";
        using var lectura = await e.ClienteAsync(true, soloLeer: true); using var ajeno = await e.ClienteAsync(true, asociar: false);
        var body = new { expedienteId = DemoSesionSeedHostedService.Id("DEMO-CE-EXPORTACION") };
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(lectura, $"{Base}/{id}/vincular", body, etag)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(ajeno, $"{Base}/{id}/vincular", body, etag)).StatusCode);
        Assert.Empty((await Json(await ajeno.GetAsync(Base))).EnumerateArray());
        var primero = await Post(admin, $"{Base}/{id}/vincular", body, etag); primero.EnsureSuccessStatusCode();
        var segundo = await Post(admin, $"{Base}/{id}/vincular", new { expedienteId = DemoSesionSeedHostedService.Id("DEMO-CE-EXPORTACION-RT") }, primero.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, segundo.StatusCode); Assert.Equal("CE_OTR_YA_VINCULADO", (await Json(segundo)).GetProperty("code").GetString());
    }
}
