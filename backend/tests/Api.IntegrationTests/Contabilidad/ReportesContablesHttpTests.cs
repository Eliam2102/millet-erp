using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// C1.7 (F1-CON-06 / CON-07) contra el API y Postgres reales: balanza por periodo, acumulada, por nivel y por sucursal; mayor con saldo
/// inicial y corrido; auxiliar por cliente; libro diario; y baja de cuenta solo con saldo en cero (C1.7-a, C1.7-b). Los datos FIX van
/// en una sucursal propia para que las cifras exactas no dependan de otras pruebas.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class ReportesContablesHttpTests(WebApplicationFactory<Program> factory)
{
    private const string Polizas = Base + "/polizas";
    private const string Reportes = Base + "/reportes";

    private static readonly string[] PermisosAnalista =
        [PermisosCanonicos.ContabilidadPolizasLeer, PermisosCanonicos.ContabilidadPolizasCapturar, PermisosCanonicos.ContabilidadMovimientosGestionarTodasSucursales];

    private static readonly string[] PermisosSupervisor =
        [PermisosCanonicos.ContabilidadPolizasLeer, PermisosCanonicos.ContabilidadPolizasAutorizar, PermisosCanonicos.ContabilidadPolizasAnular,
         PermisosCanonicos.ContabilidadMovimientosGestionarTodasSucursales];

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private sealed record Escenario(
        string Suf, HttpClient Admin, HttpClient Analista, HttpClient Supervisor, Guid Sucursal, JsonElement Raiz, JsonElement Gasto,
        JsonElement Banco, JsonElement Clientes, JsonElement SinMovimientos, Guid Cliente, Guid Tipo);

    private async Task<Escenario> CrearEscenarioAsync(DateOnly hoy, DateOnly anterior)
    {
        var suf = Sufijo();
        var admin = await LoginAsync(factory);
        await AsegurarPeriodosAbiertosAsync(admin, anterior, hoy);
        var sucursal = await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = $"RC{suf}", Nombre = $"FIX Sucursal {suf}" });
        sucursal.EnsureSuccessStatusCode();
        var raiz = await CrearCuenta(admin, Codigo(suf, "6"), "FIX gastos");
        var gasto = await CrearCuenta(admin, Codigo(suf, "6.1"), "FIX gasto", Id(raiz));
        var banco = await CrearCuenta(admin, Codigo(suf, "6.2"), "FIX bancos", Id(raiz));
        var clientes = await CrearCuenta(admin, Codigo(suf, "6.3"), "FIX clientes", Id(raiz));
        var sinMov = await CrearCuenta(admin, Codigo(suf, "6.4"), "FIX sin movimientos", Id(raiz));
        var (_, cliente) = await MotorTerceros.SembrarAsync(factory);
        var tipo = await admin.PostAsJsonAsync($"{Base}/tipos-documento", new { clave = $"FIX-{suf}", nombre = $"FIX Diario {suf}", esPrueba = true, tipoSat = "Diario" });
        Assert.Equal(HttpStatusCode.Created, tipo.StatusCode);
        var analista = await ClienteConPermisosAsync(factory, PermisosAnalista);
        var supervisor = await ClienteConPermisosAsync(factory, PermisosSupervisor);
        return new(suf, admin, analista, supervisor, Id(await Json(sucursal)), await Obtener(admin, Id(raiz)), gasto, banco, clientes, sinMov,
            cliente, Id(await Json(tipo)));
    }

    private async Task LimpiarAsync(Escenario? e)
    {
        if (e is null) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE consumidor = 'POLIZA' AND referencia IN (SELECT id::text FROM contabilidad.polizas WHERE tipo_operacion_id = {0})", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.polizas SET anulada_por_poliza_id = NULL, poliza_anulada_id = NULL WHERE tipo_operacion_id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.solicitudes_anulacion_poliza WHERE poliza_id IN (SELECT id FROM contabilidad.polizas WHERE tipo_operacion_id = {0})", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.polizas WHERE tipo_operacion_id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.consecutivos_poliza WHERE tipo_operacion_id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.tipos_documento_contable WHERE id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cuentas_contables_uso WHERE cuenta_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", $"FIX-{e.Suf}-%");
        await Limpiar(factory.Services, e.Suf);
        await LimpiarClienteAsync(e.Analista);
        await LimpiarClienteAsync(e.Supervisor);
    }

    private static object Partida(Escenario e, JsonElement cuenta, decimal cargo, decimal abono, Guid? cliente = null) =>
        new { cuentaId = Id(cuenta), concepto = $"FIX {e.Suf}", cargo, abono, sucursalId = e.Sucursal, clienteId = cliente };

    private static async Task<JsonElement> CapturarAsync(Escenario e, DateOnly fecha, params object[] partidas)
    {
        var r = await e.Analista.PostAsJsonAsync(Polizas, new { tipoOperacionId = e.Tipo, fecha, concepto = $"FIX reporte {e.Suf}", partidas });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await Json(r);
    }

    private static async Task<JsonElement> AutorizadaAsync(Escenario e, DateOnly fecha, params object[] partidas)
    {
        var p = await CapturarAsync(e, fecha, partidas);
        var r = await Send(e.Supervisor, HttpMethod.Post, $"{Polizas}/{Id(p)}/autorizar", null, Etag(p));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await Json(r);
    }

    private static async Task<JsonElement> GetAsync(HttpClient c, string url)
    {
        var r = await c.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await Json(r);
    }

    private static JsonElement Fila(JsonElement reporte, JsonElement cuenta) =>
        reporte.GetProperty("filas").EnumerateArray().Single(f => f.GetProperty("cuentaId").GetGuid() == Id(cuenta));

    private static (decimal Inicial, decimal Cargos, decimal Abonos, decimal Final) Saldos(JsonElement f) =>
        (f.GetProperty("saldoInicial").GetDecimal(), f.GetProperty("cargos").GetDecimal(), f.GetProperty("abonos").GetDecimal(),
         f.GetProperty("saldoFinal").GetDecimal());

    [Fact]
    public async Task C17_balanza_mayor_auxiliar_y_diario_cuadran_entre_si_y_la_baja_exige_saldo_cero()
    {
        var hoy = Millet.SharedKernel.Application.FechaContable.HoyLocal(DateTimeOffset.UtcNow);
        var anterior = hoy.AddMonths(-1);
        Escenario? e = null;
        HttpClient? aprobador = null;
        try
        {
            e = await CrearEscenarioAsync(hoy, anterior);
            // Mes anterior (saldo inicial): gasto 1,000 contra bancos.
            await AutorizadaAsync(e, anterior, Partida(e, e.Gasto, 1_000m, 0m), Partida(e, e.Banco, 0m, 1_000m));
            // Mes en curso: gasto 300; venta a un cliente 500; un gasto de 50 que se anula (suma cero); un borrador que no cuenta.
            await AutorizadaAsync(e, hoy, Partida(e, e.Gasto, 300m, 0m), Partida(e, e.Banco, 0m, 300m));
            await AutorizadaAsync(e, hoy, Partida(e, e.Clientes, 500m, 0m, e.Cliente), Partida(e, e.Banco, 0m, 500m));
            var anulada = await AutorizadaAsync(e, hoy, Partida(e, e.Gasto, 50m, 0m), Partida(e, e.Banco, 0m, 50m));
            // M-212: la anulación la solicita una persona y la aprueba otra.
            var solicitud = await Send(e.Supervisor, HttpMethod.Post, $"{Polizas}/{Id(anulada)}/solicitar-anulacion",
                new { motivo = "FIX importe capturado con error" }, Etag(anulada));
            Assert.Equal(HttpStatusCode.Created, solicitud.StatusCode);
            aprobador = await ClienteConPermisosAsync(factory, PermisosCanonicos.ContabilidadPolizasLeer,
                PermisosCanonicos.ContabilidadPolizasAnularAprobar, PermisosCanonicos.ContabilidadMovimientosGestionarTodasSucursales);
            var anular = await Send(aprobador, HttpMethod.Post, $"{Polizas}/solicitudes-anulacion/{Id(await Json(solicitud))}/aprobar");
            Assert.Equal(HttpStatusCode.Created, anular.StatusCode);
            await CapturarAsync(e, hoy, Partida(e, e.Gasto, 999m, 0m), Partida(e, e.Banco, 0m, 999m));

            var filtro = $"anio={hoy.Year}&periodo={hoy.Month}&sucursalId={e.Sucursal}";

            // ── Balanza del periodo (C1.7-a) ──
            var balanza = await GetAsync(e.Supervisor, $"{Reportes}/balanza?{filtro}");
            Assert.Equal((1_000m, 300m, 0m, 1_300m), Saldos(Fila(balanza, e.Gasto)));
            Assert.Equal((-1_000m, 0m, 800m, -1_800m), Saldos(Fila(balanza, e.Banco)));
            Assert.Equal((0m, 500m, 0m, 500m), Saldos(Fila(balanza, e.Clientes)));
            Assert.Equal(1_800m, Fila(balanza, e.Banco).GetProperty("saldoAcreedor").GetDecimal());
            Assert.DoesNotContain(balanza.GetProperty("filas").EnumerateArray(), f => f.GetProperty("cuentaId").GetGuid() == Id(e.SinMovimientos));
            var totales = balanza.GetProperty("totales");
            Assert.Equal(800m, totales.GetProperty("cargos").GetDecimal());
            Assert.Equal(800m, totales.GetProperty("abonos").GetDecimal());
            Assert.True(totales.GetProperty("cuadra").GetBoolean());

            // Cada cuenta: saldo en balanza = saldo final de su mayor; el mayor trae inicial, movimientos y saldo corrido.
            foreach (var cuenta in new[] { e.Gasto, e.Banco, e.Clientes })
            {
                var mayor = await GetAsync(e.Supervisor, $"{Reportes}/mayor?cuentaId={Id(cuenta)}&anio={hoy.Year}&desde={hoy.Month}&sucursalId={e.Sucursal}");
                var (inicial, cargos, abonos, final) = Saldos(Fila(balanza, cuenta));
                Assert.Equal(inicial, mayor.GetProperty("saldoInicial").GetDecimal());
                Assert.Equal(final, mayor.GetProperty("saldoFinal").GetDecimal());
                var filas = mayor.GetProperty("filas").EnumerateArray().ToList();
                Assert.Equal(cargos, filas.Sum(f => f.GetProperty("cargo").GetDecimal()));
                Assert.Equal(abonos, filas.Sum(f => f.GetProperty("abono").GetDecimal()));
                Assert.Equal(final, filas[^1].GetProperty("saldo").GetDecimal());
            }
            var mayorGasto = await GetAsync(e.Supervisor, $"{Reportes}/mayor?cuentaId={Id(e.Gasto)}&anio={hoy.Year}&desde={hoy.Month}&sucursalId={e.Sucursal}");
            Assert.Equal([1_300m, 1_350m, 1_300m], mayorGasto.GetProperty("filas").EnumerateArray().Select(f => f.GetProperty("saldo").GetDecimal()));

            // Una cuenta de mayor (título) acumula a sus hijas: las partidas propias de la empresa suman cero.
            var mayorRaiz = await GetAsync(e.Supervisor, $"{Reportes}/mayor?cuentaId={Id(e.Raiz)}&anio={hoy.Year}&desde={hoy.Month}&sucursalId={e.Sucursal}");
            Assert.Equal(0m, mayorRaiz.GetProperty("saldoFinal").GetDecimal());
            Assert.Equal(8, mayorRaiz.GetProperty("filas").GetArrayLength());

            // ── Por nivel: la raíz FIX junta a sus hijas ──
            var nivel = e.Raiz.GetProperty("nivel").GetInt32();
            var porNivel = await GetAsync(e.Supervisor, $"{Reportes}/balanza?{filtro}&nivel={nivel}");
            Assert.Equal((0m, 800m, 800m, 0m), Saldos(Fila(porNivel, e.Raiz)));
            Assert.True(porNivel.GetProperty("totales").GetProperty("cuadra").GetBoolean());

            // ── Acumulada del ejercicio: mismo saldo final; el mes anterior entra en movimientos si es del mismo año ──
            var acumulada = await GetAsync(e.Supervisor, $"{Reportes}/balanza?{filtro}&acumulada=true");
            var mismoEjercicio = anterior.Year == hoy.Year;
            Assert.Equal(mismoEjercicio ? (0m, 1_300m, 0m, 1_300m) : (1_000m, 300m, 0m, 1_300m), Saldos(Fila(acumulada, e.Gasto)));
            Assert.True(acumulada.GetProperty("totales").GetProperty("cuadra").GetBoolean());

            // ── Toda la empresa (todas las sucursales): también cuadra ──
            var empresa = await GetAsync(e.Supervisor, $"{Reportes}/balanza?anio={hoy.Year}&periodo={hoy.Month}");
            Assert.True(empresa.GetProperty("totales").GetProperty("cuadra").GetBoolean());

            // ── Auxiliar por cliente: el cliente FIX y lo que no trae cliente; suma = mayor ──
            var auxClientes = await GetAsync(e.Supervisor, $"{Reportes}/auxiliar?cuentaId={Id(e.Clientes)}&tipo=Cliente&anio={hoy.Year}&desde={hoy.Month}&sucursalId={e.Sucursal}");
            var filaCliente = auxClientes.GetProperty("filas").EnumerateArray().Single();
            Assert.Equal(e.Cliente, filaCliente.GetProperty("auxiliarId").GetGuid());
            Assert.Equal(500m, filaCliente.GetProperty("saldoFinal").GetDecimal());
            Assert.True(auxClientes.GetProperty("totales").GetProperty("cuadra").GetBoolean());
            var auxBancos = await GetAsync(e.Supervisor, $"{Reportes}/auxiliar?cuentaId={Id(e.Banco)}&tipo=Cliente&anio={hoy.Year}&desde={hoy.Month}&sucursalId={e.Sucursal}");
            Assert.Equal("Sin auxiliar", auxBancos.GetProperty("filas").EnumerateArray().Single().GetProperty("nombre").GetString());
            Assert.Equal(-1_800m, auxBancos.GetProperty("totales").GetProperty("sinAuxiliar").GetDecimal());

            // ── Diario: 4 pólizas del mes (la anulada y su anulación incluidas; el borrador no) ──
            var diario = await GetAsync(e.Supervisor, $"{Reportes}/diario?anio={hoy.Year}&desde={hoy.Month}&sucursalId={e.Sucursal}&limit=2");
            Assert.Equal(4, diario.GetProperty("total").GetInt32());
            Assert.Equal(2, diario.GetProperty("polizas").GetArrayLength());
            var td = diario.GetProperty("totales");
            Assert.Equal((4, 8, 800m, 800m, true), (td.GetProperty("polizas").GetInt32(), td.GetProperty("partidas").GetInt32(),
                td.GetProperty("cargos").GetDecimal(), td.GetProperty("abonos").GetDecimal(), td.GetProperty("cuadra").GetBoolean()));
            Assert.All(diario.GetProperty("polizas").EnumerateArray(), p => Assert.NotEqual(JsonValueKind.Null, p.GetProperty("folio").ValueKind));

            // ── Rango inválido ──
            var invalido = await e.Supervisor.GetAsync($"{Reportes}/mayor?cuentaId={Id(e.Gasto)}&anio={hoy.Year}&desde=5&hasta=2");
            Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);

            // ── C1.7-b: la cuenta con saldo no se da de baja; la que está en cero sí ──
            var conSaldo = await Send(e.Admin, HttpMethod.Post, $"{Base}/cuentas/{Id(e.Gasto)}/desactivar", null, Etag(e.Gasto));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, conSaldo.StatusCode);
            Assert.Equal("CONTAB_CUENTA_BAJA_CON_SALDO", await Code(conSaldo));
            var enCero = await Send(e.Admin, HttpMethod.Post, $"{Base}/cuentas/{Id(e.SinMovimientos)}/desactivar", null, Etag(e.SinMovimientos));
            Assert.Equal(HttpStatusCode.OK, enCero.StatusCode);
            Assert.False((await Json(enCero)).GetProperty("activa").GetBoolean());
        }
        finally
        {
            if (aprobador is not null) await LimpiarClienteAsync(aprobador);
            await LimpiarAsync(e);
        }
    }

    [Fact]
    public async Task C17_los_reportes_piden_permiso_de_lectura_de_polizas()
    {
        var sinPermiso = await ClienteConPermisosAsync(factory, PermisosCanonicos.ContabilidadCatalogoLeer);
        try
        {
            var r = await sinPermiso.GetAsync($"{Reportes}/balanza?anio=2026&periodo=1");
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        finally { await LimpiarClienteAsync(sinPermiso); }
    }
}
