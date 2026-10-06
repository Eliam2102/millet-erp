using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
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
        Contar(factory.Services, "periodos_contables_bitacora", $"periodo_id = '{periodoId}'" + (accion is { } a ? $" AND accion = {(short)a}" : string.Empty));

    private async Task LimpiarPeriodosAsync(params int[] anios)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        foreach (var anio in anios)
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.periodos_contables_bitacora WHERE periodo_id IN (SELECT id FROM contabilidad.periodos_contables WHERE anio = {0})", anio);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.periodos_contables WHERE anio = {0}", anio);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.ejercicios_contables WHERE anio = {0}", anio);
        }
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

            // Sin permiso de lectura no ve los periodos.
            var ajeno = await ClienteConPermisosAsync(factory, PermisosCanonicos.ContabilidadDimensionesLeer);
            Assert.Equal(HttpStatusCode.Forbidden, (await ajeno.GetAsync($"{Periodos}/ejercicios")).StatusCode);
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
            Assert.Equal(p.GetProperty("version").GetInt32(), ultima.GetProperty("versionResultante").GetInt32());
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
