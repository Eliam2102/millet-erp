using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Millet.Contabilidad.Infrastructure.Persistence.Migrations;
using Millet.SharedKernel.Domain.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Almacen.Application.Cierre;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Contabilidad.Application.Periodos;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Contabilidad.Infrastructure.PublicAdapters;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// F1-CON-03 contra el API y Postgres reales (plan §10, casos 1–9): movimiento en periodo abierto y cerrado, cierre concurrente,
/// reapertura sin permiso, cierre repetido e idempotencia, consumidores de Ola 1A, reapertura autorizada con bitácora y auditoría,
/// independencia del inventario y periodo 13. Cada prueba usa un año lejano propio (no el calendario oficial, que es por confirmar)
/// y lo borra al terminar: nunca cierra el año en curso, del que dependen las pruebas de dimensiones.
/// </summary>
public class PeriodosHttpTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Periodos = Base + "/periodos";
    private const string Motivo = "FIX cierre de prueba del periodo";
    private static readonly int[] EneroYAjuste = [1, 13];
    private static readonly int[] SoloEnero = [1];

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private static readonly int[] SoloTrece = [13];
    private static readonly string[] Historial = ["Abrir", "Cerrar", "Reabrir"];
    private static readonly HttpStatusCode[] UnoYUnConflicto = [HttpStatusCode.OK, HttpStatusCode.Conflict];

    private static int AnioPrueba() => Random.Shared.Next(2100, 2990);

    private static JsonElement Periodo(JsonElement ejercicio, int numero) =>
        ejercicio.GetProperty("periodos").EnumerateArray().Single(p => p.GetProperty("numero").GetInt32() == numero);

    private static async Task<JsonElement> CrearEjercicioAsync(HttpClient admin, int anio, params int[] abrir)
    {
        var r = await admin.PostAsJsonAsync($"{Periodos}/ejercicios", new { anio });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var e = await Json(r);
        if (abrir.Length == 0) return e;
        var abierto = await Send(admin, HttpMethod.Post, $"{Periodos}/ejercicios/{Id(e)}/abrir", new { numeros = abrir }, Etag(e));
        Assert.Equal(HttpStatusCode.OK, abierto.StatusCode);
        return await Json(abierto);
    }

    private static Task<HttpResponseMessage> Cerrar(HttpClient c, JsonElement periodo, string motivo = Motivo, string? idempotencyKey = null) =>
        Transicion(c, "cerrar", periodo, motivo, idempotencyKey);

    private static Task<HttpResponseMessage> Reabrir(HttpClient c, JsonElement periodo, string motivo = "FIX reapertura autorizada de prueba") =>
        Transicion(c, "reabrir", periodo, motivo, null);

    private static async Task<HttpResponseMessage> Transicion(HttpClient c, string accion, JsonElement periodo, string motivo, string? idempotencyKey)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Periodos}/{Id(periodo)}/{accion}") { Content = JsonContent.Create(new { motivo }) };
        req.Headers.TryAddWithoutValidation("If-Match", Etag(periodo));
        if (idempotencyKey is not null) req.Headers.Add("Idempotency-Key", idempotencyKey);
        return await c.SendAsync(req);
    }

    /// <summary>Cierra en orden los periodos indicados (cierre secuencial) y devuelve el ejercicio actualizado.</summary>
    private static async Task<JsonElement> CerrarEnOrdenAsync(HttpClient admin, Guid ejercicioId, params int[] numeros)
    {
        foreach (var n in numeros)
        {
            var e = await Json(await admin.GetAsync($"{Periodos}/ejercicios/{ejercicioId}"));
            Assert.Equal(HttpStatusCode.OK, (await Cerrar(admin, Periodo(e, n))).StatusCode);
        }
        return await Json(await admin.GetAsync($"{Periodos}/ejercicios/{ejercicioId}"));
    }

    private Task<long> Bitacora(Guid periodoId, AccionPeriodo? accion = null) =>
        Escalar<long>(factory.Services, $"SELECT count(*) FROM core.audit_log WHERE modulo = 'Contabilidad' AND entidad = 'PeriodoContable' AND entidad_id = '{periodoId}' AND operacion IN ('abrir', 'cerrar', 'reabrir')" + (accion is { } a ? $" AND operacion = '{a.ToString().ToLowerInvariant()}'" : string.Empty));

    private async Task LimpiarPeriodosAsync(params int[] anios)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        foreach (var anio in anios)
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.periodos_contables WHERE anio = {0}", anio);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.ejercicios_contables WHERE anio = {0}", anio);
        }
    }

    [Fact]
    public async Task Migracion_conserva_historial_previo_y_su_reversa_no_duplica_la_auditoria()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Esquema aislado dentro de una transacción: no altera el esquema que usan las demás pruebas.
        var schema = "fix_periodos_" + Guid.NewGuid().ToString("N");
        var periodoId = Guid.NewGuid();
        var historialId = Guid.NewGuid();
        var migration = new HistorialPeriodosEnAuditoriaCentral();
        string Aislado(string sql) => sql.Replace("contabilidad.", schema + ".", StringComparison.Ordinal)
            .Replace("core.audit_log", schema + ".audit_log", StringComparison.Ordinal);
        async Task Ejecutar(string sql) => await db.Database.ExecuteSqlRawAsync(Aislado(sql));
        async Task<long> ContarAislado(string sql) => await db.Database.SqlQueryRaw<long>(Aislado(sql)).SingleAsync();

        var crearEsquema = "CREATE SCHEMA " + schema + "; CREATE TABLE " + schema + ".audit_log (LIKE core.audit_log INCLUDING ALL);"
            + " CREATE TABLE " + schema + ".periodos_contables (id uuid PRIMARY KEY, empresa_id uuid NOT NULL, anio integer NOT NULL, numero integer NOT NULL);";
        await db.Database.ExecuteSqlRawAsync(crearEsquema);
        var seedPeriodo = $"INSERT INTO contabilidad.periodos_contables VALUES ('{periodoId}', '{EmpresaBootstrapId}', 2026, 1)";
        await Ejecutar(seedPeriodo);
        var down = migration.DownOperations.OfType<SqlOperation>().Single().Sql;
        var up = migration.UpOperations.OfType<SqlOperation>().Single().Sql;
        await Ejecutar(down); // reconstruye la tabla anterior, todavía vacía
        var seed = $"""
            INSERT INTO contabilidad.periodos_contables_bitacora
                (id, empresa_id, periodo_id, accion, estado_anterior, estado_nuevo, motivo,
                 usuario_id, usuario_nombre, ocurrido_en, version_resultante, version, created_at, updated_at)
            VALUES ('{historialId}', '{EmpresaBootstrapId}', '{periodoId}', 2, 1, 2,
                'FIX cierre histórico conservado', NULL, 'Contadora histórica FIX', now(), 3, 1, now(), now())
            """;
        await Ejecutar(seed);
        await Ejecutar(up);
        await Ejecutar("DROP TABLE contabilidad.periodos_contables_bitacora");
        Assert.Equal(1, await ContarAislado($"SELECT count(*) AS \"Value\" FROM core.audit_log WHERE id = '{historialId}' AND entidad_id = '{periodoId}' AND operacion = 'cerrar' AND metadatos->>'Motivo' = 'FIX cierre histórico conservado' AND metadatos->>'VersionResultante' = '3' AND actor_nombre = 'Contadora histórica FIX'"));
        await Ejecutar(down);
        Assert.Equal(1, await ContarAislado($"SELECT count(*) AS \"Value\" FROM contabilidad.periodos_contables_bitacora WHERE id = '{historialId}' AND motivo = 'FIX cierre histórico conservado' AND version_resultante = 3"));
        await Ejecutar(up);
        Assert.Equal(1, await ContarAislado("SELECT count(*) AS \"Value\" FROM core.audit_log"));
        await transaction.RollbackAsync();
    }

    private sealed class RechazarAuditoriaCierre : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ContabilidadDbContext db
                && db.ChangeTracker.Entries<AuditLogEntry>().Any(e => e.State == EntityState.Added && e.Entity.Operacion == "cerrar"))
                throw new InvalidOperationException("FIX fallo al persistir auditoría central");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Fallo_de_auditoria_central_revierte_el_cierre_y_no_agrega_historial()
    {
        var anio = AnioPrueba();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<ContabilidadDbContext>((_, options) => options.AddInterceptors(new RechazarAuditoriaCierre()))));
        using var admin = await LoginAsync(factory);
        using var fallido = await LoginAsync(host);
        try
        {
            var ejercicio = await CrearEjercicioAsync(admin, anio, 1);
            var enero = Periodo(ejercicio, 1);
            Assert.Equal(HttpStatusCode.InternalServerError, (await Cerrar(fallido, enero)).StatusCode);
            var actual = Periodo(await Json(await admin.GetAsync($"{Periodos}/ejercicios/{Id(ejercicio)}")), 1);
            Assert.Equal("Abierto", actual.GetProperty("estado").GetString());
            Assert.Equal(Etag(enero), Etag(actual));
            Assert.Equal(1, await Bitacora(Id(enero)));
            // El error libera el candado y no consume la versión: un cierre posterior puede persistirse.
            Assert.Equal(HttpStatusCode.OK, (await Cerrar(admin, actual)).StatusCode);
            Assert.Equal(2, await Bitacora(Id(enero)));
        }
        finally { await LimpiarPeriodosAsync(anio); }
    }

    [Fact]
    public async Task Historial_central_excluye_registros_de_otra_empresa_y_conserva_el_permiso_contable()
    {
        var anio = AnioPrueba();
        using var admin = await LoginAsync(factory);
        try
        {
            var enero = Periodo(await CrearEjercicioAsync(admin, anio, 1), 1);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO core.audit_log (id, timestamp, empresa_id, modulo, entidad, entidad_id, aggregate_root_id,
                        operacion, cambios, correlation_id, es_bulk, actor_nombre, actor_tipo, entidad_etiqueta, resumen, metadatos)
                    SELECT {Guid.NewGuid()}, timestamp, {Guid.NewGuid()}, modulo, entidad, entidad_id, aggregate_root_id,
                        operacion, cambios, correlation_id, es_bulk, actor_nombre, actor_tipo, entidad_etiqueta, resumen, metadatos
                    FROM core.audit_log WHERE entidad_id = {Id(enero)} AND operacion = 'abrir'
                    """);
            }
            var historial = await admin.GetAsync($"{Periodos}/{Id(enero)}/bitacora");
            Assert.Equal(HttpStatusCode.OK, historial.StatusCode);
            Assert.Single((await Json(historial)).EnumerateArray());
            Assert.Equal(HttpStatusCode.NotFound,
                (await admin.GetAsync($"{Periodos}/{Guid.NewGuid()}/bitacora")).StatusCode);
        }
        finally { await LimpiarPeriodosAsync(anio); }
    }

    // ── Movimiento mínimo (cuenta afectable, tipo de documento, sucursal; sin reglas ni centros) ──

    private sealed record Movimiento(string Suf, Guid Cuenta, Guid Tipo, Guid Sucursal);

    private static async Task<Movimiento> CrearMovimientoBaseAsync(HttpClient admin)
    {
        var suf = Sufijo();
        var raiz = await CrearCuenta(admin, Codigo(suf, "6"), "FIX gastos de periodo");
        var hoja = await CrearCuenta(admin, Codigo(suf, "6.1"), "FIX gasto de periodo", Id(raiz));
        var tipo = await Json(await admin.PostAsJsonAsync($"{Base}/tipos-documento", new { clave = $"FIX-{suf}", nombre = $"FIX Póliza {suf}", esPrueba = true }));
        var suc = await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = $"CP{suf}", Nombre = $"FIX Sucursal {suf}" });
        suc.EnsureSuccessStatusCode();
        return new(suf, Id(hoja), Id(tipo), Id(await Json(suc)));
    }

    private static object Mov(Movimiento m, DateOnly fecha) =>
        new { cuentaId = m.Cuenta, tipoDocumentoId = m.Tipo, fechaContable = fecha, sucursalId = m.Sucursal, referencia = $"FIX-{m.Suf}" };

    private async Task LimpiarMovimientoAsync(Movimiento? m)
    {
        if (m is null) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.movimientos_dimension_prueba WHERE cuenta_codigo LIKE {0}", $"FIX-{m.Suf}-%");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.tipos_documento_contable WHERE clave = {0}", $"FIX-{m.Suf}");
        await Limpiar(factory.Services, m.Suf);
    }

    private static async Task<string[]> CodigosValidacion(HttpClient c, object mov)
    {
        var r = await c.PostAsJsonAsync($"{Base}/movimientos/validar", mov);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var v = await Json(r);
        var errores = v.GetProperty("errores").EnumerateArray().ToList();
        Assert.Equal(errores.Count == 0, v.GetProperty("valido").GetBoolean());
        Assert.All(errores.Where(e => e.GetProperty("codigo").GetString()!.StartsWith("CONTAB_PERIODO_", StringComparison.Ordinal)),
            e => Assert.Equal("fechaContable", e.GetProperty("campo").GetString()));
        return [.. errores.Select(e => e.GetProperty("codigo").GetString()!)];
    }

    private sealed class PausaConsulta(DateOnly fecha)
    {
        public DateOnly Fecha { get; } = fecha;
        public TaskCompletionSource Leido { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continuar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // Pausa DESPUÉS de leer el estado real: reproduce la ventana entre consultar periodo y guardar movimiento.
    private sealed class ConsultaPausada(IPeriodoContableConsultaPort inner, PausaConsulta pausa) : IPeriodoContableConsultaPort
    {
        public Task<EstadoPeriodoContable> ConsultarAsync(int anio, int numero, CancellationToken ct) => inner.ConsultarAsync(anio, numero, ct);

        public async Task<EstadoPeriodoContable> ConsultarPorFechaAsync(DateOnly fecha, CancellationToken ct)
        {
            var estado = await inner.ConsultarPorFechaAsync(fecha, ct);
            if (fecha == pausa.Fecha)
            {
                pausa.Leido.TrySetResult();
                await pausa.Continuar.Task.WaitAsync(ct);
            }
            return estado;
        }
    }

    private async Task<bool> EsperarCierreBloqueadoAsync(Task<HttpResponseMessage> cerrar)
    {
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!cerrar.IsCompleted)
        {
            if (await Escalar<long>(factory.Services,
                "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND classid = 0 AND objid = 218300417") > 0)
                return true;
            await Task.Delay(25, limite.Token);
        }
        return false;
    }

    [Fact]
    public async Task Cierre_espera_al_movimiento_que_ya_verifico_el_periodo_y_luego_rechaza_nuevos_movimientos()
    {
        var anio = AnioPrueba();
        Movimiento? m = null;
        var fecha = new DateOnly(anio, 1, 15);
        var pausa = new PausaConsulta(fecha);
        Task<HttpResponseMessage>? confirmar = null, cerrar = null;
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IPeriodoContableConsultaPort>();
            s.AddScoped<IPeriodoContableConsultaPort>(sp =>
                new ConsultaPausada(new PeriodoContableConsultaAdapter(sp.GetRequiredService<ContabilidadDbContext>()), pausa));
        }));
        using var admin = await LoginAsync(factory);
        using var clienteMovimiento = await LoginAsync(host);
        try
        {
            m = await CrearMovimientoBaseAsync(admin);
            var enero = Periodo(await CrearEjercicioAsync(admin, anio, 1), 1);

            confirmar = clienteMovimiento.PostAsJsonAsync($"{Base}/movimientos-prueba", Mov(m, fecha));
            await pausa.Leido.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cerrar = Cerrar(admin, enero);
            Assert.True(await EsperarCierreBloqueadoAsync(cerrar));
            Assert.False(cerrar.IsCompleted);

            pausa.Continuar.TrySetResult();
            Assert.Equal(HttpStatusCode.Created, (await confirmar).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await cerrar).StatusCode);
            Assert.Equal(1, await Contar(factory.Services, "movimientos_dimension_prueba", $"cuenta_codigo LIKE 'FIX-{m.Suf}-%'"));
            var rechazado = await admin.PostAsJsonAsync($"{Base}/movimientos-prueba", Mov(m, fecha));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rechazado.StatusCode);
            Assert.Equal("CONTAB_PERIODO_CERRADO", await Code(rechazado));
            Assert.Equal(1, await Contar(factory.Services, "movimientos_dimension_prueba", $"cuenta_codigo LIKE 'FIX-{m.Suf}-%'"));
        }
        finally
        {
            pausa.Continuar.TrySetResult();
            if (confirmar is not null) await confirmar;
            if (cerrar is not null) await cerrar;
            await LimpiarMovimientoAsync(m);
            await LimpiarPeriodosAsync(anio);
        }
    }

    [Fact]
    public async Task Apertura_de_lote_invalido_no_guarda_transiciones_bitacora_ni_version_y_libera_el_candado()
    {
        var anio = AnioPrueba();
        try
        {
            using var admin = await LoginAsync(factory);
            var ejercicio = await CrearEjercicioAsync(admin, anio);
            var respuesta = await Send(admin, HttpMethod.Post, $"{Periodos}/ejercicios/{Id(ejercicio)}/abrir",
                new { numeros = EneroYAjuste }, Etag(ejercicio));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
            Assert.Equal("CONTAB_PERIODO_13_REQUIERE_12_CERRADO", await Code(respuesta));
            var sinCambios = await Json(await admin.GetAsync($"{Periodos}/ejercicios/{Id(ejercicio)}"));
            Assert.Equal(Etag(ejercicio), Etag(sinCambios));
            Assert.All(sinCambios.GetProperty("periodos").EnumerateArray(), p => Assert.Equal("NoAbierto", p.GetProperty("estado").GetString()));
            Assert.Equal(Etag(Periodo(ejercicio, 1)), Etag(Periodo(sinCambios, 1)));
            Assert.Equal(0, await Bitacora(Id(Periodo(ejercicio, 1))));
            var valido = await Send(admin, HttpMethod.Post, $"{Periodos}/ejercicios/{Id(ejercicio)}/abrir",
                new { numeros = SoloEnero }, Etag(ejercicio));
            Assert.Equal(HttpStatusCode.OK, valido.StatusCode);
            Assert.Equal(1, await Bitacora(Id(Periodo(ejercicio, 1))));
        }
        finally { await LimpiarPeriodosAsync(anio); }
    }

    // ── Casos 1, 2 y 6: el movimiento de prueba y el panel respetan el contrato ──

    [Fact]
    public async Task Movimiento_en_periodo_abierto_se_guarda_y_en_cerrado_no_abierto_o_inexistente_se_rechaza()
    {
        var anio = AnioPrueba();
        Movimiento? m = null;
        try
        {
            var admin = await LoginAsync(factory);
            m = await CrearMovimientoBaseAsync(admin);
            var e = await CrearEjercicioAsync(admin, anio, 1);
            var enero = new DateOnly(anio, 1, 15);
            var filtro = $"cuenta_codigo LIKE 'FIX-{m.Suf}-%'";

            // 1. Periodo abierto: se valida y se guarda.
            Assert.Empty(await CodigosValidacion(admin, Mov(m, enero)));
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"{Base}/movimientos-prueba", Mov(m, enero))).StatusCode);
            Assert.Equal(1, await Contar(factory.Services, "movimientos_dimension_prueba", filtro));

            // 6. No abierto (febrero) e inexistente (año sin ejercicio): el panel lo muestra y la confirmación lo rechaza.
            var casos = new[] { (new DateOnly(anio, 2, 10), "CONTAB_PERIODO_NO_ABIERTO"), (new DateOnly(anio + 1, 1, 10), "CONTAB_PERIODO_INEXISTENTE") };
            foreach (var (fecha, codigo) in casos)
            {
                Assert.Equal(codigo, Assert.Single(await CodigosValidacion(admin, Mov(m, fecha))));
                var r = await admin.PostAsJsonAsync($"{Base}/movimientos-prueba", Mov(m, fecha));
                Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
                Assert.Equal(codigo, await Code(r));
            }

            // 2. Periodo cerrado: 422 con mensaje claro y nada escrito.
            Assert.Equal(HttpStatusCode.OK, (await Cerrar(admin, Periodo(e, 1))).StatusCode);
            var cerrado = await admin.PostAsJsonAsync($"{Base}/movimientos-prueba", Mov(m, enero));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, cerrado.StatusCode);
            var p = await Json(cerrado);
            Assert.Equal("CONTAB_PERIODO_CERRADO", p.GetProperty("code").GetString());
            Assert.Equal($"El periodo {anio}-01 está cerrado; no se pueden registrar movimientos con esa fecha.", p.GetProperty("detail").GetString());
            Assert.Equal("CONTAB_PERIODO_CERRADO", Assert.Single(await CodigosValidacion(admin, Mov(m, enero))));
            Assert.Equal(1, await Contar(factory.Services, "movimientos_dimension_prueba", filtro));

            // Estado por HTTP: mismo contrato que el puerto.
            var estado = await Json(await admin.GetAsync($"{Periodos}/estado?fecha={enero:yyyy-MM-dd}"));
            Assert.Equal("Cerrado", estado.GetProperty("estado").GetString());
            Assert.False(estado.GetProperty("admiteMovimientos").GetBoolean());
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{Periodos}/estado")).StatusCode);
        }
        finally
        {
            await LimpiarMovimientoAsync(m);
            await LimpiarPeriodosAsync(anio);
        }
    }

    // ── Caso 3: cierre concurrente ──

    [Fact]
    public async Task Cierre_concurrente_con_la_misma_version_da_un_200_un_409_y_una_sola_fila_de_bitacora()
    {
        var anio = AnioPrueba();
        try
        {
            var admin = await LoginAsync(factory);
            var otro = await LoginAsync(factory);
            var enero = Periodo(await CrearEjercicioAsync(admin, anio, 1), 1);

            var respuestas = await Task.WhenAll(Cerrar(admin, enero), Cerrar(otro, enero, "FIX cierre simultáneo de otra sesión"));
            Assert.Equal(UnoYUnConflicto, respuestas.Select(r => r.StatusCode).Order().ToArray());
            Assert.Equal(1, await Bitacora(Id(enero), AccionPeriodo.Cerrar));
            Assert.Equal(enero.GetProperty("version").GetInt32() + 1,
                (await Json(respuestas.Single(r => r.StatusCode == HttpStatusCode.OK))).GetProperty("version").GetInt32());
        }
        finally { await LimpiarPeriodosAsync(anio); }
    }

    // ── Caso 4: reapertura sin permiso ──

    [Fact]
    public async Task Rol_operativo_cierra_pero_no_reabre_y_el_estado_no_cambia()
    {
        var anio = AnioPrueba();
        try
        {
            var admin = await LoginAsync(factory);
            var ejercicio = await CrearEjercicioAsync(admin, anio, 1);
            var operativo = await ClienteConPermisosAsync(factory, PermisosCanonicos.ContabilidadPeriodoLeer, PermisosCanonicos.ContabilidadPeriodoCerrar);

            Assert.Equal(HttpStatusCode.Forbidden, (await operativo.PostAsJsonAsync($"{Periodos}/ejercicios", new { anio = anio + 1 })).StatusCode);
            var cerrado = await Cerrar(operativo, Periodo(ejercicio, 1));
            Assert.Equal(HttpStatusCode.OK, cerrado.StatusCode);

            var reabrir = await Reabrir(operativo, await Json(cerrado));
            Assert.Equal(HttpStatusCode.Forbidden, reabrir.StatusCode);
            var estado = await Json(await operativo.GetAsync($"{Periodos}/estado?anio={anio}&numero=1"));
            Assert.Equal("Cerrado", estado.GetProperty("estado").GetString());
            Assert.Equal(0, await Bitacora(Id(Periodo(ejercicio, 1)), AccionPeriodo.Reabrir));
            Assert.Equal(HttpStatusCode.OK,
                (await operativo.GetAsync($"{Periodos}/{Id(Periodo(ejercicio, 1))}/bitacora")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await operativo.GetAsync("/api/v1/admin/auditoria?desde=2026-10-01&hasta=2026-10-31")).StatusCode);

            // Sin permiso de lectura no ve los periodos.
            var ajeno = await ClienteConPermisosAsync(factory, PermisosCanonicos.ContabilidadDimensionesLeer);
            Assert.Equal(HttpStatusCode.Forbidden, (await ajeno.GetAsync($"{Periodos}/ejercicios")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await ajeno.GetAsync($"{Periodos}/{Id(Periodo(ejercicio, 1))}/bitacora")).StatusCode);
        }
        finally { await LimpiarPeriodosAsync(anio, anio + 1); }
    }

    // ── Caso 5: cierre repetido e idempotencia ──

    [Fact]
    public async Task Cerrar_un_periodo_ya_cerrado_es_409_explicito_y_la_misma_clave_devuelve_la_respuesta_original()
    {
        var anio = AnioPrueba();
        try
        {
            var admin = await LoginAsync(factory);
            var enero = Periodo(await CrearEjercicioAsync(admin, anio, 1), 1);
            var clave = Guid.NewGuid().ToString("D");

            var primero = await Cerrar(admin, enero, idempotencyKey: clave);
            Assert.Equal(HttpStatusCode.OK, primero.StatusCode);
            var cerrado = await Json(primero);

            // Misma Idempotency-Key: la respuesta original (mismo ETag), sin segunda fila.
            var replay = await Cerrar(admin, enero, idempotencyKey: clave);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(primero.Headers.ETag, replay.Headers.ETag);

            // Clave nueva con la versión vigente: conflicto explícito, nunca éxito silencioso.
            var repetido = await Cerrar(admin, cerrado);
            Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
            Assert.Equal("CONTAB_PERIODO_YA_CERRADO", await Code(repetido));
            // Versión vieja: conflicto de concurrencia.
            Assert.Equal("CONCURRENCY_CONFLICT", await Code(await Cerrar(admin, enero)));
            Assert.Equal(1, await Bitacora(Id(enero), AccionPeriodo.Cerrar));

            // Motivo corto: 400; sin If-Match: 428.
            Assert.Equal(HttpStatusCode.BadRequest, (await Reabrir(admin, cerrado, "corto")).StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionRequired,
                (await admin.PostAsJsonAsync($"{Periodos}/{Id(enero)}/reabrir", new { motivo = "FIX sin versión del periodo" })).StatusCode);
        }
        finally { await LimpiarPeriodosAsync(anio); }
    }

    // ── Casos 7 y 8: reapertura autorizada, bitácora y auditoría; el inventario no se reabre ──

    [Fact]
    public async Task Reapertura_autorizada_deja_bitacora_y_auditoria_y_no_reabre_el_inventario()
    {
        var anio = AnioPrueba();
        Guid? almacenId = null;
        try
        {
            var admin = await LoginAsync(factory);
            var ejercicio = await CrearEjercicioAsync(admin, anio, 1, 2);
            var enero = Periodo(ejercicio, 1);

            // Inventario de enero cerrado en Almacén (su propia tabla, D18).
            using (var scope = factory.Services.CreateScope())
            {
                var almacen = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
                var cierre = new PeriodoCerrado(Guid.CreateVersion7(), EmpresaBootstrapId, anio, 1, Guid.NewGuid());
                almacen.PeriodosCerrados.Add(cierre);
                await almacen.SaveChangesAsync();
                almacenId = cierre.Id;
            }

            var ej = await CerrarEnOrdenAsync(admin, Id(ejercicio), 1, 2);
            // Reabrir enero con febrero cerrado: «reabra primero febrero».
            var orden = await Reabrir(admin, Periodo(ej, 1));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, orden.StatusCode);
            Assert.Equal("CONTAB_PERIODO_SIGUIENTE_CERRADO", await Code(orden));
            Assert.Equal(HttpStatusCode.OK, (await Reabrir(admin, Periodo(ej, 2))).StatusCode);

            ej = await Json(await admin.GetAsync($"{Periodos}/ejercicios/{Id(ejercicio)}"));
            var reabierto = await Reabrir(admin, Periodo(ej, 1), "FIX ajuste de provisión omitida en enero");
            Assert.Equal(HttpStatusCode.OK, reabierto.StatusCode);
            var p = await Json(reabierto);
            Assert.Equal("Abierto", p.GetProperty("estado").GetString());
            Assert.Equal("dev-superadmin", p.GetProperty("reabiertoPor").GetString());
            Assert.Equal($"\"{p.GetProperty("version").GetInt32()}\"", reabierto.Headers.ETag!.Tag);

            var bitacora = (await Json(await admin.GetAsync($"{Periodos}/{Id(enero)}/bitacora"))).EnumerateArray().ToList();
            Assert.Equal(Historial, bitacora.Select(b => b.GetProperty("accion").GetString()).ToArray());
            var ultima = bitacora[^1];
            Assert.Equal("FIX ajuste de provisión omitida en enero", ultima.GetProperty("motivo").GetString());
            Assert.Equal("dev-superadmin", ultima.GetProperty("usuarioNombre").GetString());
            Assert.Equal("Cerrado", ultima.GetProperty("estadoAnterior").GetString());
            Assert.Equal("Abierto", ultima.GetProperty("estadoNuevo").GetString());
            Assert.Equal(3, await Bitacora(Id(enero)));
            // El historial de la pantalla proviene del mismo registro central, no de una tabla del módulo.
            Assert.Equal(1, await Escalar<long>(factory.Services,
                $"SELECT count(*) FROM core.audit_log WHERE id = '{Id(ultima)}' AND entidad_id = '{Id(enero)}' AND operacion = 'reabrir' AND metadatos->>'Motivo' = 'FIX ajuste de provisión omitida en enero'"));
            Assert.Equal(0, await Escalar<long>(factory.Services,
                "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'contabilidad' AND table_name = 'periodos_contables_bitacora'"));
            Assert.Equal(p.GetProperty("version").GetInt32(), ultima.GetProperty("versionResultante").GetInt32());
            var dia = DateOnly.FromDateTime(DateTime.UtcNow);
            var auditoria = await admin.GetAsync($"/api/v1/admin/auditoria?desde={dia:yyyy-MM-dd}&hasta={dia:yyyy-MM-dd}&modulo=Contabilidad&recurso=PeriodoContable&entidadId={Id(enero)}&accion=reabrir");
            Assert.Equal(HttpStatusCode.OK, auditoria.StatusCode);
            var filaCentral = Assert.Single((await Json(auditoria)).GetProperty("items").EnumerateArray());
            Assert.Equal(Id(ultima), Id(filaCentral));
            using var cambios = JsonDocument.Parse(filaCentral.GetProperty("cambios").GetString()!);
            Assert.Equal("FIX ajuste de provisión omitida en enero",
                cambios.RootElement.GetProperty("diff").GetProperty("Motivo").GetProperty("despues").GetString());
            Assert.True(await Escalar<long>(factory.Services,
                $"SELECT count(*) FROM core.audit_log WHERE entidad_id = '{Id(enero)}' AND entidad = '{nameof(PeriodoContable)}'") >= 3);

            // D18: el cierre de Almacén sigue ahí y su movimiento de enero sigue rechazado.
            using (var scope = factory.Services.CreateScope())
            {
                var almacen = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
                Assert.True(await almacen.PeriodosCerrados.AsNoTracking().AnyAsync(x => x.Id == almacenId));
                var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                    PeriodoCerradoValidator.LanzarSiCerradoAsync(almacen, EmpresaBootstrapId, new DateOnly(anio, 1, 15), default));
                Assert.Equal("PERIODO_CERRADO", ex.Code);
            }
        }
        finally
        {
            if (almacenId is { } id)
            {
                using var scope = factory.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AlmacenDbContext>().Database
                    .ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.periodos_cerrados WHERE id = {id}");
            }
            await LimpiarPeriodosAsync(anio);
        }
    }

    // ── Caso 9: periodo 13 ──

    [Fact]
    public async Task Periodo_13_no_abre_con_diciembre_abierto_y_solo_admite_movimientos_manuales()
    {
        var anio = AnioPrueba();
        try
        {
            var admin = await LoginAsync(factory);
            var meses = Enumerable.Range(1, 12).ToArray();
            var ejercicio = await CrearEjercicioAsync(admin, anio, meses);
            Assert.Equal($"{anio}-12-31", Periodo(ejercicio, 13).GetProperty("fechaInicio").GetString());

            var antes = await Send(admin, HttpMethod.Post, $"{Periodos}/ejercicios/{Id(ejercicio)}/abrir", new { numeros = SoloTrece }, Etag(ejercicio));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, antes.StatusCode);
            Assert.Equal("CONTAB_PERIODO_13_REQUIERE_12_CERRADO", await Code(antes));

            var ej = await CerrarEnOrdenAsync(admin, Id(ejercicio), meses);
            var abierto = await Send(admin, HttpMethod.Post, $"{Periodos}/ejercicios/{Id(ejercicio)}/abrir",
                new { numeros = SoloTrece, motivo = "FIX ajustes de auditoría" }, Etag(ej));
            Assert.Equal(HttpStatusCode.OK, abierto.StatusCode);
            Assert.Equal("Abierto", Periodo(await Json(abierto), 13).GetProperty("estado").GetString());
            // Una fecha de diciembre resuelve al 12 (cerrado), nunca al 13.
            var dic = await Json(await admin.GetAsync($"{Periodos}/estado?fecha={anio}-12-31"));
            Assert.Equal(12, dic.GetProperty("numero").GetInt32());
            Assert.Equal("Cerrado", dic.GetProperty("estado").GetString());

            using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
            using var scope = enEmpresa.Services.CreateScope();
            var verificador = scope.ServiceProvider.GetRequiredService<VerificadorPeriodoContable>();
            await verificador.LanzarSiNoAdmiteAsync(anio, 13, OrigenMovimiento.Manual, default);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => verificador.LanzarSiNoAdmiteAsync(anio, 13, OrigenMovimiento.AuxiliarCxC, default));
            Assert.Equal("CONTAB_PERIODO_13_SOLO_MANUAL", ex.Code);
        }
        finally { await LimpiarPeriodosAsync(anio); }
    }
}
