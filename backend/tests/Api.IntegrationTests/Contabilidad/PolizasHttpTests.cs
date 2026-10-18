using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// F1-CON-04 contra el API y Postgres reales: captura y autorización de pólizas manuales (C1.3-a/b/c, CA10.12), numeración por tipo
/// sin huecos ni duplicados (K10.9-a/b/c) y anulación en negativo (CA10.5, K10.3-a/b, periodo cerrado, motor por documento). Datos
/// FIX con tipos de prueba propios: el consecutivo de cada prueba arranca en 1. Las pruebas que cierran periodos usan un año lejano.
/// </summary>
public class PolizasHttpTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Polizas = Base + "/polizas";
    private const string Motivo = "FIX importe capturado con error";
    private static readonly int[] PrimerTrimestre = [1, 2, 3];
    private static readonly int[] SoloTrece = [13];

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private static DateOnly Hoy() => FechaHoy();

    private static DateOnly FechaHoy() => Millet.SharedKernel.Application.FechaContable.HoyLocal(DateTimeOffset.UtcNow);

    private sealed record Escenario(string Suf, HttpClient Admin, HttpClient Analista, HttpClient Supervisor, Guid Sucursal, Guid Gasto, Guid Banco, Guid Tipo, string Clave)
    {
        public List<int> Anios { get; } = [];
    }

    private static readonly string[] PermisosAnalista =
        [PermisosCanonicos.ContabilidadPolizasLeer, PermisosCanonicos.ContabilidadPolizasCapturar, PermisosCanonicos.ContabilidadMovimientosGestionarTodasSucursales];

    private static readonly string[] PermisosSupervisor =
        [PermisosCanonicos.ContabilidadPolizasLeer, PermisosCanonicos.ContabilidadPolizasAutorizar, PermisosCanonicos.ContabilidadPolizasAnular,
         PermisosCanonicos.ContabilidadMovimientosGestionarTodasSucursales];

    private async Task<Escenario> CrearEscenarioAsync()
    {
        var suf = Sufijo();
        var admin = await LoginAsync(factory);
        await AsegurarPeriodosAbiertosAsync(admin, Hoy());
        var sucursal = await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = $"PZ{suf}", Nombre = $"FIX Sucursal {suf}" });
        sucursal.EnsureSuccessStatusCode();
        var raiz = await CrearCuenta(admin, Codigo(suf, "6"), "FIX gastos");
        var gasto = await CrearCuenta(admin, Codigo(suf, "6.1"), "FIX gasto de mantenimiento", Id(raiz));
        var banco = await CrearCuenta(admin, Codigo(suf, "6.2"), "FIX bancos", Id(raiz));
        var clave = $"FIX-{suf}";
        var tipo = await admin.PostAsJsonAsync($"{Base}/tipos-documento", new { clave, nombre = $"FIX Asiento {suf}", esPrueba = true, tipoSat = "Diario" });
        Assert.Equal(HttpStatusCode.Created, tipo.StatusCode);
        var analista = await ClienteConPermisosAsync(factory, PermisosAnalista);
        var supervisor = await ClienteConPermisosAsync(factory, PermisosSupervisor);
        return new(suf, admin, analista, supervisor, Id(await Json(sucursal)), Id(gasto), Id(banco), Id(await Json(tipo)), clave);
    }

    private async Task LimpiarAsync(Escenario? e)
    {
        if (e is null) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        var like = $"FIX-{e.Suf}-%";
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE consumidor = 'POLIZA' AND referencia IN (SELECT id::text FROM contabilidad.polizas WHERE tipo_operacion_id = {0})", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.polizas WHERE tipo_operacion_id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.consecutivos_poliza WHERE tipo_operacion_id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.tipos_documento_contable WHERE id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cuentas_contables_uso WHERE cuenta_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", like);
        await Limpiar(factory.Services, e.Suf);
        foreach (var anio in e.Anios)
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.periodos_contables WHERE anio = {0}", anio);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.ejercicios_contables WHERE anio = {0}", anio);
        }
        await LimpiarClienteAsync(e.Analista);
        await LimpiarClienteAsync(e.Supervisor);
    }

    private static object Partida(Escenario e, Guid cuenta, decimal cargo, decimal abono) =>
        new { cuentaId = cuenta, concepto = $"FIX {e.Suf}", cargo, abono, sucursalId = e.Sucursal };

    private static object Poliza(Escenario e, DateOnly fecha, decimal cargo = 1000m, decimal abono = 1000m, int? periodoNumero = null) => new
    {
        tipoOperacionId = e.Tipo, fecha, concepto = $"FIX provisión {e.Suf}", periodoNumero,
        partidas = new[] { Partida(e, e.Gasto, cargo, 0m), Partida(e, e.Banco, 0m, abono) },
    };

    private static async Task<JsonElement> CapturarAsync(Escenario e, DateOnly fecha, decimal cargo = 1000m, decimal abono = 1000m, int? periodoNumero = null)
    {
        var r = await e.Analista.PostAsJsonAsync(Polizas, Poliza(e, fecha, cargo, abono, periodoNumero));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var p = await Json(r);
        Assert.Equal("Borrador", p.GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, p.GetProperty("folio").ValueKind);
        return p;
    }

    private static Task<HttpResponseMessage> AutorizarAsync(HttpClient c, JsonElement p) =>
        Send(c, HttpMethod.Post, $"{Polizas}/{Id(p)}/autorizar", null, Etag(p));

    private static async Task<JsonElement> AutorizadaAsync(Escenario e, DateOnly fecha, decimal importe = 1000m)
    {
        var r = await AutorizarAsync(e.Supervisor, await CapturarAsync(e, fecha, importe, importe));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await Json(r);
    }

    private static Task<HttpResponseMessage> AnularAsync(HttpClient c, JsonElement p, string motivo = Motivo, int? anio = null, int? numero = null) =>
        Send(c, HttpMethod.Post, $"{Polizas}/{Id(p)}/anular", new { motivo, periodoAnio = anio, periodoNumero = numero }, Etag(p));

    private static async Task<JsonElement> ObtenerAsync(HttpClient c, Guid id) => await Json(await c.GetAsync($"{Polizas}/{id}"));

    private static async Task<Dictionary<Guid, (decimal Cargos, decimal Abonos)>> SaldosAsync(Escenario e, int anio, int numero) =>
        (await Json(await e.Supervisor.GetAsync($"{Polizas}/saldos?anio={anio}&numero={numero}"))).EnumerateArray()
            .Where(s => s.GetProperty("cuentaId").GetGuid() == e.Gasto || s.GetProperty("cuentaId").GetGuid() == e.Banco)
            .ToDictionary(s => s.GetProperty("cuentaId").GetGuid(), s => (s.GetProperty("cargos").GetDecimal(), s.GetProperty("abonos").GetDecimal()));

    // ── K10.9 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task K109a_dos_polizas_del_mismo_tipo_toman_folios_consecutivos_y_el_tipo_nuevo_arranca_en_uno()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var primera = await AutorizadaAsync(e, Hoy());
            var segunda = await AutorizadaAsync(e, Hoy());
            Assert.Equal($"{e.Clave}-000001", primera.GetProperty("folio").GetString());
            Assert.Equal($"{e.Clave}-000002", segunda.GetProperty("folio").GetString());
            Assert.Equal("Autorizada", primera.GetProperty("estado").GetString());

            // K10.9-c: un tipo nuevo agregado al catálogo toma su propio consecutivo.
            var otro = await e.Admin.PostAsJsonAsync($"{Base}/tipos-documento", new { clave = $"{e.Clave}B", nombre = "FIX tipo nuevo", esPrueba = true, tipoSat = "Egreso" });
            Assert.Equal(HttpStatusCode.Created, otro.StatusCode);
            var otroId = Id(await Json(otro));
            try
            {
                var body = new
                {
                    tipoOperacionId = otroId, fecha = Hoy(), concepto = "FIX tipo nuevo",
                    partidas = new[] { Partida(e, e.Gasto, 50m, 0m), Partida(e, e.Banco, 0m, 50m) },
                };
                var borrador = await Json(await e.Analista.PostAsJsonAsync(Polizas, body));
                var autorizada = await Json(await AutorizarAsync(e.Supervisor, borrador));
                Assert.Equal($"{e.Clave}B-000001", autorizada.GetProperty("folio").GetString());
                Assert.Equal("Egreso", autorizada.GetProperty("tipoSat").GetString());

                // La bandeja encuentra la póliza por su folio.
                var bandeja = await Json(await e.Supervisor.GetAsync($"{Polizas}?q={e.Clave}B-1"));
                Assert.Equal(Id(autorizada), Id(bandeja.GetProperty("items").EnumerateArray().Single()));
            }
            finally
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.polizas WHERE tipo_operacion_id = {0}", otroId);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.consecutivos_poliza WHERE tipo_operacion_id = {0}", otroId);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.tipos_documento_contable WHERE id = {0}", otroId);
            }
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task K109b_cincuenta_polizas_autorizadas_al_mismo_tiempo_quedan_sin_huecos_ni_duplicados()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var borradores = new List<JsonElement>();
            for (var i = 0; i < 50; i++) borradores.Add(await CapturarAsync(e, Hoy(), 10m + i, 10m + i));

            var respuestas = await Task.WhenAll(borradores.Select(b => AutorizarAsync(e.Supervisor, b)));
            Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            var numeros = new List<int>();
            foreach (var r in respuestas) numeros.Add((await Json(r)).GetProperty("numero").GetInt32());
            Assert.Equal(Enumerable.Range(1, 50), numeros.Order());
            Assert.Equal(50, await Escalar<int>(factory.Services,
                $"SELECT ultimo_numero FROM contabilidad.consecutivos_poliza WHERE tipo_operacion_id = '{e.Tipo}' AND anio = {Hoy().Year}"));
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task Un_tipo_real_lleva_clave_de_dos_letras()
    {
        var admin = await LoginAsync(factory);
        var r = await admin.PostAsJsonAsync($"{Base}/tipos-documento", new { clave = "TTX", nombre = "FIX clave inválida", esPrueba = false, tipoSat = "Diario" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // ── C1.3 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task C13_cuadre_segregacion_e_inmutabilidad()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();

            // C1.3-a: un borrador que no cuadra se guarda, pero no se autoriza.
            var descuadrada = await CapturarAsync(e, Hoy(), 1000m, 900m);
            Assert.False(descuadrada.GetProperty("cuadra").GetBoolean());
            var r = await AutorizarAsync(e.Supervisor, descuadrada);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Equal("CONTAB_POLIZA_NO_CUADRA", await Code(r));

            // Se corrige el borrador y ya cuadra.
            var corregida = await Send(e.Analista, HttpMethod.Put, $"{Polizas}/{Id(descuadrada)}", Poliza(e, Hoy()), Etag(descuadrada));
            Assert.Equal(HttpStatusCode.OK, corregida.StatusCode);
            var borrador = await Json(corregida);
            Assert.True(borrador.GetProperty("cuadra").GetBoolean());

            // C1.3-c: el analista no autoriza su propia póliza (ni con el permiso).
            var analistaConAutorizar = await ClienteConPermisosAsync(factory, [.. PermisosAnalista, PermisosCanonicos.ContabilidadPolizasAutorizar]);
            try
            {
                var propia = await Json(await analistaConAutorizar.PostAsJsonAsync(Polizas, Poliza(e, Hoy())));
                var rechazo = await AutorizarAsync(analistaConAutorizar, propia);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, rechazo.StatusCode);
                Assert.Equal("CONTAB_POLIZA_AUTORIZA_QUIEN_CAPTURA", await Code(rechazo));
            }
            finally { await LimpiarClienteAsync(analistaConAutorizar); }

            // Sin el permiso de autorizar: 403.
            Assert.Equal(HttpStatusCode.Forbidden, (await AutorizarAsync(e.Analista, borrador)).StatusCode);

            var autorizada = await Json(await AutorizarAsync(e.Supervisor, borrador));
            Assert.Equal(1, autorizada.GetProperty("numero").GetInt32());
            Assert.Equal(1, await Contar(factory.Services, "cuentas_contables_uso", $"cuenta_id = '{e.Gasto}' AND consumidor = 'POLIZA'"));

            // C1.3-b: la autorizada no se edita ni se descarta.
            var editar = await Send(e.Analista, HttpMethod.Put, $"{Polizas}/{Id(autorizada)}", Poliza(e, Hoy(), 5m, 5m), Etag(autorizada));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, editar.StatusCode);
            Assert.Equal("CONTAB_POLIZA_NO_EDITABLE", await Code(editar));
            var descartar = await Send(e.Analista, HttpMethod.Delete, $"{Polizas}/{Id(autorizada)}", null, Etag(autorizada));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, descartar.StatusCode);

            // Un borrador sí se descarta y no consume número.
            var otro = await CapturarAsync(e, Hoy());
            Assert.Equal(HttpStatusCode.NoContent, (await Send(e.Analista, HttpMethod.Delete, $"{Polizas}/{Id(otro)}", null, Etag(otro))).StatusCode);
            Assert.Equal(2, (await AutorizadaAsync(e, Hoy())).GetProperty("numero").GetInt32());

            // La autoría queda en la auditoría central (bitácora, R23).
            await AssertCreacionesAuditadasAsync(factory.Services, "polizas", "Poliza", $"r.tipo_operacion_id = '{e.Tipo}' AND r.numero = 2", "Concepto", 1);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task Las_partidas_se_validan_contra_el_catalogo_con_el_campo_de_cada_renglon()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var body = new
            {
                tipoOperacionId = e.Tipo, fecha = Hoy(), concepto = "FIX cuenta inexistente",
                partidas = new[] { Partida(e, e.Gasto, 10m, 0m), Partida(e, Guid.NewGuid(), 0m, 10m) },
            };
            var r = await e.Analista.PostAsJsonAsync(Polizas, body);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains("partidas[1].cuentaId", (await Json(r)).GetRawText());
        }
        finally { await LimpiarAsync(e); }
    }

    // ── K10.3 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CA105_K103_la_anulacion_lleva_las_mismas_partidas_en_negativo_y_la_balanza_queda_como_si_no_existiera()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var hoy = Hoy();
            var antes = await SaldosAsync(e, hoy.Year, hoy.Month);
            var original = await AutorizadaAsync(e, hoy, 1234.56m);
            var con = await SaldosAsync(e, hoy.Year, hoy.Month);
            Assert.Equal(1234.56m, con[e.Gasto].Cargos);

            // Sin el permiso de anular: 403. Sin motivo: 400.
            Assert.Equal(HttpStatusCode.Forbidden, (await AnularAsync(e.Analista, original)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await AnularAsync(e.Supervisor, original, "corto")).StatusCode);

            var r = await AnularAsync(e.Supervisor, original);
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            var anulacion = await Json(r);
            var folioOriginal = original.GetProperty("folio").GetString();
            Assert.Equal($"Anulación de {folioOriginal}", anulacion.GetProperty("concepto").GetString());
            Assert.Equal($"{e.Clave}-000002", anulacion.GetProperty("folio").GetString());
            Assert.Equal("Autorizada", anulacion.GetProperty("estado").GetString());
            Assert.Equal(Id(original), anulacion.GetProperty("polizaAnuladaId").GetGuid());
            Assert.Equal(folioOriginal, anulacion.GetProperty("polizaAnuladaFolio").GetString());
            var partidas = anulacion.GetProperty("partidas").EnumerateArray().ToList();
            Assert.Equal(-1234.56m, partidas[0].GetProperty("cargo").GetDecimal());
            Assert.Equal(0m, partidas[0].GetProperty("abono").GetDecimal());
            Assert.Equal(0m, partidas[1].GetProperty("cargo").GetDecimal());
            Assert.Equal(-1234.56m, partidas[1].GetProperty("abono").GetDecimal());

            var anulada = await ObtenerAsync(e.Supervisor, Id(original));
            Assert.Equal("Anulada", anulada.GetProperty("estado").GetString());
            Assert.Equal(Id(anulacion), anulada.GetProperty("anuladaPorPolizaId").GetGuid());
            Assert.Equal(Motivo, anulada.GetProperty("motivoAnulacion").GetString());

            // K10.3-a: los saldos del periodo quedan como si la póliza no existiera.
            var despues = await SaldosAsync(e, hoy.Year, hoy.Month);
            Assert.Equal(antes.GetValueOrDefault(e.Gasto), despues[e.Gasto]);
            Assert.Equal(antes.GetValueOrDefault(e.Banco), despues[e.Banco]);
            Assert.Equal((0m, 0m), despues[e.Gasto]);

            // K10.3-b: otra anulación se impide (también la de la propia anulación).
            var segunda = await AnularAsync(e.Supervisor, anulada);
            Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
            Assert.Equal("CONTAB_POLIZA_YA_ANULADA", await Code(segunda));
            var deAnulacion = await AnularAsync(e.Supervisor, anulacion);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, deAnulacion.StatusCode);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task K103b_dos_anulaciones_concurrentes_dejan_una_sola()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var original = await AutorizadaAsync(e, Hoy());
            var r = await Task.WhenAll(AnularAsync(e.Supervisor, original), AnularAsync(e.Supervisor, original));
            Assert.Single(r, x => x.StatusCode == HttpStatusCode.Created);
            Assert.Single(r, x => x.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(1, await Contar(factory.Services, "polizas", $"poliza_anulada_id = '{Id(original)}'"));
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task Con_el_periodo_original_cerrado_la_anulacion_va_al_periodo_abierto_indicado()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var anio = Random.Shared.Next(2100, 2990);
            e.Anios.Add(anio);
            var ejercicio = await Json(await e.Admin.PostAsJsonAsync($"{Base}/periodos/ejercicios", new { anio }));
            ejercicio = await Json(await Send(e.Admin, HttpMethod.Post, $"{Base}/periodos/ejercicios/{Id(ejercicio)}/abrir",
                new { numeros = PrimerTrimestre }, Etag(ejercicio)));
            var original = await AutorizadaAsync(e, new DateOnly(anio, 1, 20));
            var enero = ejercicio.GetProperty("periodos").EnumerateArray().Single(p => p.GetProperty("numero").GetInt32() == 1);
            (await Send(e.Admin, HttpMethod.Post, $"{Base}/periodos/{Id(enero)}/cerrar", new { motivo = "FIX cierre de enero de prueba" }, Etag(enero)))
                .EnsureSuccessStatusCode();

            var sinDestino = await AnularAsync(e.Supervisor, original);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, sinDestino.StatusCode);
            Assert.Equal("CONTAB_POLIZA_ANULACION_REQUIERE_PERIODO", await Code(sinDestino));
            var aCerrado = await AnularAsync(e.Supervisor, original, anio: anio, numero: 1);
            Assert.Equal("CONTAB_PERIODO_CERRADO", await Code(aCerrado));

            var r = await AnularAsync(e.Supervisor, original, anio: anio, numero: 2);
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            var anulacion = await Json(r);
            Assert.Equal(2, anulacion.GetProperty("periodoNumero").GetInt32());
            Assert.Equal(new DateOnly(anio, 2, 1), DateOnly.Parse(anulacion.GetProperty("fecha").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(2, anulacion.GetProperty("numero").GetInt32()); // consecutivo del año del periodo destino

            // Con el periodo original abierto no se elige otro destino.
            var otra = await AutorizadaAsync(e, new DateOnly(anio, 3, 5));
            var conDestino = await AnularAsync(e.Supervisor, otra, anio: anio, numero: 2);
            Assert.Equal("CONTAB_POLIZA_ANULACION_PERIODO_ORIGINAL_ABIERTO", await Code(conDestino));
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task CA1012_el_periodo_13_acepta_polizas_manuales_y_rechaza_las_automaticas()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var anio = Random.Shared.Next(2100, 2990);
            e.Anios.Add(anio);
            var ejercicio = await Json(await e.Admin.PostAsJsonAsync($"{Base}/periodos/ejercicios", new { anio }));
            ejercicio = await Json(await Send(e.Admin, HttpMethod.Post, $"{Base}/periodos/ejercicios/{Id(ejercicio)}/abrir",
                new { numeros = Enumerable.Range(1, 12).ToArray() }, Etag(ejercicio)));
            foreach (var n in Enumerable.Range(1, 12))
            {
                var p = (await Json(await e.Admin.GetAsync($"{Base}/periodos/ejercicios/{Id(ejercicio)}"))).GetProperty("periodos").EnumerateArray()
                    .Single(x => x.GetProperty("numero").GetInt32() == n);
                (await Send(e.Admin, HttpMethod.Post, $"{Base}/periodos/{Id(p)}/cerrar", new { motivo = "FIX cierre secuencial de prueba" }, Etag(p))).EnsureSuccessStatusCode();
            }
            ejercicio = await Json(await e.Admin.GetAsync($"{Base}/periodos/ejercicios/{Id(ejercicio)}"));
            (await Send(e.Admin, HttpMethod.Post, $"{Base}/periodos/ejercicios/{Id(ejercicio)}/abrir",
                new { numeros = SoloTrece, motivo = "FIX ajustes de auditoría" }, Etag(ejercicio))).EnsureSuccessStatusCode();

            var ajuste = await AutorizarAsync(e.Supervisor, await CapturarAsync(e, new DateOnly(anio, 12, 31), periodoNumero: 13));
            Assert.Equal(HttpStatusCode.OK, ajuste.StatusCode);
            Assert.Equal(13, (await Json(ajuste)).GetProperty("periodoNumero").GetInt32());

            using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
            using var scope = enEmpresa.Services.CreateScope();
            var puerto = scope.ServiceProvider.GetRequiredService<IPolizaContablePort>();
            var ex = await Assert.ThrowsAnyAsync<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>(() => puerto.RegistrarAutomaticaAsync(
                Automatica(e, new DateOnly(anio, 12, 31), Guid.NewGuid()), default));
            Assert.Equal("CONTAB_PERIODO_CERRADO", ex.Code); // la fecha 31-dic cae en el 12 (cerrado); el 13 nunca se resuelve por fecha
        }
        finally { await LimpiarAsync(e); }
    }

    // ── Motor (puerto público) ──────────────────────────────────────────────

    private static SolicitudPolizaAutomatica Automatica(Escenario e, DateOnly fecha, Guid documentoId, decimal importe = 116m) => new(
        e.Clave, fecha, $"FIX factura de proveedor {e.Suf}",
        new DocumentoOrigenPoliza("cuentas_por_pagar", "factura_proveedor", documentoId, $"A-{e.Suf}", null),
        $"cuentas_por_pagar.factura.registrada.v1:{documentoId}",
        [new PartidaRequest(e.Gasto, null, importe, 0m, e.Sucursal), new PartidaRequest(e.Banco, null, 0m, importe, e.Sucursal)]);

    [Fact]
    public async Task El_motor_numera_con_el_mismo_consecutivo_es_idempotente_y_al_cancelar_el_documento_anula_en_negativo()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
            using var scope = enEmpresa.Services.CreateScope();
            var puerto = scope.ServiceProvider.GetRequiredService<IPolizaContablePort>();
            var doc1 = Guid.NewGuid();
            var doc2 = Guid.NewGuid();

            // K10.9-a con el motor: dos facturas seguidas, folios seguidos.
            var p1 = await puerto.RegistrarAutomaticaAsync(Automatica(e, Hoy(), doc1), default);
            var p2 = await puerto.RegistrarAutomaticaAsync(Automatica(e, Hoy(), doc2), default);
            Assert.Equal($"{e.Clave}-000001", p1.Folio);
            Assert.Equal($"{e.Clave}-000002", p2.Folio);
            Assert.Equal(EstadoPoliza.Autorizada, p1.Estado);

            // Misma llave, misma póliza.
            var repetida = await puerto.RegistrarAutomaticaAsync(Automatica(e, Hoy(), doc1), default);
            Assert.True(repetida.Idempotente);
            Assert.Equal(p1.Id, repetida.Id);

            // K10.3 en el motor: la cancelación del documento anula su póliza con la misma regla; repetir no duplica.
            var anuladas = await puerto.AnularPorDocumentoOrigenAsync("cuentas_por_pagar", "factura_proveedor", doc1, "FIX CFDI cancelado ante el SAT", default);
            var anulacion = Assert.Single(anuladas);
            Assert.Equal($"{e.Clave}-000003", anulacion.Folio);
            var otraVez = await puerto.AnularPorDocumentoOrigenAsync("cuentas_por_pagar", "factura_proveedor", doc1, "FIX CFDI cancelado ante el SAT", default);
            Assert.True(Assert.Single(otraVez).Idempotente);

            var detalle = await ObtenerAsync(e.Supervisor, anulacion.Id);
            Assert.Equal($"Anulación de {e.Clave}-000001", detalle.GetProperty("concepto").GetString());
            Assert.Equal("Automatica", detalle.GetProperty("origen").GetString());
            Assert.Equal(-116m, detalle.GetProperty("totalCargos").GetDecimal());
        }
        finally { await LimpiarAsync(e); }
    }
}
