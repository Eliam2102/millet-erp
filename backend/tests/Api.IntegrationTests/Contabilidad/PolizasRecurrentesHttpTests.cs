using System.Globalization;
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
/// K10.4 contra el API y Postgres reales: plantilla recurrente que genera la póliza de cada periodo sin capturarla de nuevo
/// (CA10.6), reversa automática en negativo el día 1 del periodo siguiente (K10.4-b), activación por otra persona (R23),
/// idempotencia del generador, pendiente cuando el periodo no existe, y reclasificación ligada al origen en ambos sentidos (K10.4-a).
/// Las plantillas usan un año pasado propio (todos sus meses ya llegaron) y se borra al terminar.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class PolizasRecurrentesHttpTests(WebApplicationFactory<Program> factory)
{
    private const string Polizas = Base + "/polizas";
    private const string Recurrentes = Base + "/polizas-recurrentes";
    private static readonly int[] DoceMeses = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private sealed record Escenario(string Suf, HttpClient Admin, HttpClient Analista, HttpClient Supervisor, Guid Sucursal, Guid Gasto, Guid Banco, Guid Tipo, string Clave)
    {
        public List<int> Anios { get; } = [];
    }

    private static readonly string[] PermisosAnalista =
        [PermisosCanonicos.ContabilidadRecurrentesLeer, PermisosCanonicos.ContabilidadRecurrentesAdministrar,
         PermisosCanonicos.ContabilidadPolizasLeer, PermisosCanonicos.ContabilidadPolizasCapturar, PermisosCanonicos.ContabilidadMovimientosGestionarTodasSucursales];

    private static readonly string[] PermisosSupervisor =
        [PermisosCanonicos.ContabilidadRecurrentesLeer, PermisosCanonicos.ContabilidadRecurrentesAutorizar, PermisosCanonicos.ContabilidadRecurrentesAdministrar,
         PermisosCanonicos.ContabilidadPolizasLeer, PermisosCanonicos.ContabilidadPolizasAutorizar, PermisosCanonicos.ContabilidadPolizasAnular,
         PermisosCanonicos.ContabilidadMovimientosGestionarTodasSucursales];

    private async Task<Escenario> CrearEscenarioAsync()
    {
        var suf = Sufijo();
        var admin = await LoginAsync(factory);
        await AsegurarPeriodosAbiertosAsync(admin, FechaHoy());
        var sucursal = await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = $"PR{suf}", Nombre = $"FIX Sucursal {suf}" });
        sucursal.EnsureSuccessStatusCode();
        var raiz = await CrearCuenta(admin, Codigo(suf, "6"), "FIX gastos");
        var gasto = await CrearCuenta(admin, Codigo(suf, "6.1"), "FIX renta de oficina", Id(raiz));
        var banco = await CrearCuenta(admin, Codigo(suf, "6.2"), "FIX provisión", Id(raiz));
        var clave = $"FIX-{suf}";
        var tipo = await admin.PostAsJsonAsync($"{Base}/tipos-documento", new { clave, nombre = $"FIX Asiento {suf}", esPrueba = true, tipoSat = "Diario" });
        Assert.Equal(HttpStatusCode.Created, tipo.StatusCode);
        var analista = await ClienteConPermisosAsync(factory, PermisosAnalista);
        var supervisor = await ClienteConPermisosAsync(factory, PermisosSupervisor);
        return new(suf, admin, analista, supervisor, Id(await Json(sucursal)), Id(gasto), Id(banco), Id(await Json(tipo)), clave);
    }

    private static DateOnly FechaHoy() => Millet.SharedKernel.Application.FechaContable.HoyLocal(DateTimeOffset.UtcNow);

    private static async Task AbrirAnioAsync(Escenario e, int anio)
    {
        e.Anios.Add(anio);
        var ejercicio = await Json(await e.Admin.PostAsJsonAsync($"{Base}/periodos/ejercicios", new { anio }));
        (await Send(e.Admin, HttpMethod.Post, $"{Base}/periodos/ejercicios/{Id(ejercicio)}/abrir", new { numeros = DoceMeses }, Etag(ejercicio)))
            .EnsureSuccessStatusCode();
    }

    private async Task LimpiarAsync(Escenario? e)
    {
        if (e is null) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE consumidor = 'POLIZA' AND referencia IN (SELECT id::text FROM contabilidad.polizas WHERE tipo_operacion_id = {0})", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.polizas WHERE tipo_operacion_id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.plantillas_recurrentes WHERE tipo_operacion_id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.consecutivos_poliza WHERE tipo_operacion_id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.tipos_documento_contable WHERE id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cuentas_contables_uso WHERE cuenta_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", $"FIX-{e.Suf}-%");
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
        new { cuentaId = cuenta, concepto = (string?)null, cargo, abono, sucursalId = e.Sucursal };

    private static object Plantilla(Escenario e, int anio, int periodos = 12, bool reversa = true, bool autorizada = true, int dia = 0) => new
    {
        nombre = $"FIX renta mensual {e.Suf}", tipoOperacionId = e.Tipo, concepto = "Renta de oficina", anioInicio = anio, mesInicio = 1,
        numeroPeriodos = periodos, diaDelMes = dia, reversaAutomatica = reversa, naceAutorizada = autorizada,
        partidas = new[] { Partida(e, e.Gasto, 15000m, 0m), Partida(e, e.Banco, 0m, 15000m) },
    };

    private static async Task<JsonElement> CrearPlantillaAsync(HttpClient c, object body)
    {
        var r = await c.PostAsJsonAsync(Recurrentes, body);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var p = await Json(r);
        Assert.Equal("Borrador", p.GetProperty("estado").GetString());
        return p;
    }

    private static Task<HttpResponseMessage> Accion(HttpClient c, JsonElement plantilla, string accion) =>
        Send(c, HttpMethod.Post, $"{Recurrentes}/{Id(plantilla)}/{accion}", null, Etag(plantilla));

    private static async Task<JsonElement> ObtenerAsync(HttpClient c, string url) => await Json(await c.GetAsync(url));

    private static DateOnly Fecha(JsonElement e, string campo) => DateOnly.Parse(e.GetProperty(campo).GetString()!, CultureInfo.InvariantCulture);

    [Fact]
    public async Task CA106_K104b_la_recurrente_genera_cada_periodo_y_su_reversa_en_negativo_el_dia_1_sin_duplicar()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var anio = Random.Shared.Next(2001, 2010);
            await AbrirAnioAsync(e, anio);
            var plantilla = await CrearPlantillaAsync(e.Analista, Plantilla(e, anio));

            // R23: quien no tiene el permiso no activa (403) y quien la preparó tampoco, aunque lo tenga (422).
            Assert.Equal(HttpStatusCode.Forbidden, (await Accion(e.Analista, plantilla, "activar")).StatusCode);
            var autor = await ClienteConPermisosAsync(factory, [.. PermisosAnalista, PermisosCanonicos.ContabilidadRecurrentesAutorizar]);
            try
            {
                var propia = await CrearPlantillaAsync(autor, Plantilla(e, anio, periodos: 1, reversa: false));
                var rechazo = await Accion(autor, propia, "activar");
                Assert.Equal(HttpStatusCode.UnprocessableEntity, rechazo.StatusCode);
                Assert.Equal("CONTAB_RECURRENTE_ACTIVA_QUIEN_CREA", await Code(rechazo));
            }
            finally { await LimpiarClienteAsync(autor); }

            // Al activarla se genera todo lo que ya tocó: 12 pólizas y 11 reversas (la de diciembre cae en un año sin periodos).
            var activa = await Accion(e.Supervisor, plantilla, "activar");
            Assert.Equal(HttpStatusCode.OK, activa.StatusCode);
            var detalle = await Json(activa);
            Assert.Equal("Activa", detalle.GetProperty("estado").GetString());
            Assert.Equal(12, detalle.GetProperty("generadas").GetInt32());
            var aplicaciones = detalle.GetProperty("aplicaciones").EnumerateArray().ToList();
            Assert.All(aplicaciones, a => Assert.Equal("Autorizada", a.GetProperty("estado").GetString()));
            Assert.Equal(new DateOnly(anio, 2, DateTime.DaysInMonth(anio, 2)), Fecha(aplicaciones[1], "fecha")); // último día del mes
            Assert.Equal(11, aplicaciones.Count(a => a.GetProperty("reversaId").ValueKind == JsonValueKind.String));
            Assert.Equal(JsonValueKind.Null, aplicaciones[11].GetProperty("reversaId").ValueKind);

            // La reversa de enero: día 1 de febrero, mismas partidas en negativo y mismo lado, ligada a la original.
            var enero = await ObtenerAsync(e.Supervisor, $"{Polizas}/{aplicaciones[0].GetProperty("polizaId").GetGuid()}");
            var reversa = await ObtenerAsync(e.Supervisor, $"{Polizas}/{aplicaciones[0].GetProperty("reversaId").GetGuid()}");
            Assert.Equal(new DateOnly(anio, 2, 1), Fecha(reversa, "fecha"));
            Assert.Equal($"Reversa de {enero.GetProperty("folio").GetString()}", reversa.GetProperty("concepto").GetString());
            Assert.Equal("Reversa", reversa.GetProperty("relacion").GetString());
            Assert.Equal(Id(enero), reversa.GetProperty("polizaRelacionadaId").GetGuid());
            var pr = reversa.GetProperty("partidas").EnumerateArray().ToList();
            Assert.Equal(-15000m, pr[0].GetProperty("cargo").GetDecimal());
            Assert.Equal(-15000m, pr[1].GetProperty("abono").GetDecimal());
            Assert.Equal("Autorizada", enero.GetProperty("estado").GetString()); // la original no se anula
            Assert.Contains(enero.GetProperty("ligadas").EnumerateArray(), l => Id(l) == Id(reversa) && l.GetProperty("relacion").GetString() == "Reversa");
            Assert.Equal(Id(plantilla), enero.GetProperty("plantillaRecurrenteId").GetGuid());

            // Idempotencia: generar otra vez no crea nada; la reversa de diciembre queda pendiente con su motivo.
            var otra = await Json(await e.Supervisor.PostAsync($"{Recurrentes}/generar?plantillaId={Id(plantilla)}", null));
            Assert.Empty(otra.GetProperty("generadas").EnumerateArray());
            var pendiente = Assert.Single(otra.GetProperty("pendientes").EnumerateArray());
            Assert.True(pendiente.GetProperty("esReversa").GetBoolean());
            Assert.Equal("CONTAB_PERIODO_INEXISTENTE", pendiente.GetProperty("codigo").GetString());
            Assert.Equal(23, await Contar(factory.Services, "polizas", $"plantilla_recurrente_id = '{Id(plantilla)}'"));

            // Con la reversa vigente, anular la original la negaría dos veces: se impide.
            var anular = await Send(e.Supervisor, HttpMethod.Post, $"{Polizas}/{Id(enero)}/anular", new { motivo = "FIX anulación indebida" }, Etag(enero));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, anular.StatusCode);
            Assert.Equal("CONTAB_POLIZA_TIENE_REVERSA", await Code(anular));

            // Una plantilla activa no se edita.
            var activaActual = await ObtenerAsync(e.Supervisor, $"{Recurrentes}/{Id(plantilla)}");
            var editar = await Send(e.Analista, HttpMethod.Put, $"{Recurrentes}/{Id(plantilla)}", Plantilla(e, anio), Etag(activaActual));
            Assert.Equal("CONTAB_RECURRENTE_NO_EDITABLE", await Code(editar));
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task En_borrador_cada_poliza_espera_autorizacion_y_solo_la_autorizada_se_revierte()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var anio = Random.Shared.Next(2010, 2020);
            await AbrirAnioAsync(e, anio);
            var plantilla = await CrearPlantillaAsync(e.Analista, Plantilla(e, anio, periodos: 2, autorizada: false, dia: 15));
            var detalle = await Json(await Accion(e.Supervisor, plantilla, "activar"));
            var aplicaciones = detalle.GetProperty("aplicaciones").EnumerateArray().ToList();
            Assert.All(aplicaciones, a => Assert.Equal("Borrador", a.GetProperty("estado").GetString()));
            Assert.All(aplicaciones, a => Assert.Equal(JsonValueKind.Null, a.GetProperty("folio").ValueKind));
            Assert.Equal(new DateOnly(anio, 1, 15), Fecha(aplicaciones[0], "fecha"));

            // El supervisor autoriza la de enero (la capturó quien preparó la plantilla) y la siguiente corrida genera su reversa.
            var enero = await ObtenerAsync(e.Supervisor, $"{Polizas}/{aplicaciones[0].GetProperty("polizaId").GetGuid()}");
            Assert.Equal(HttpStatusCode.OK, (await Send(e.Supervisor, HttpMethod.Post, $"{Polizas}/{Id(enero)}/autorizar", null, Etag(enero))).StatusCode);
            var r = await Json(await e.Supervisor.PostAsync($"{Recurrentes}/generar?plantillaId={Id(plantilla)}", null));
            var generada = Assert.Single(r.GetProperty("generadas").EnumerateArray());
            Assert.True(generada.GetProperty("esReversa").GetBoolean());
            Assert.Equal(2, generada.GetProperty("mes").GetInt32());
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task K104a_la_reclasificacion_queda_ligada_y_se_ve_desde_la_poliza_original()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var hoy = FechaHoy();
            object Poliza(Guid cargo, Guid abono, Guid? reclasifica = null) => new
            {
                tipoOperacionId = e.Tipo, fecha = hoy, concepto = reclasifica is null ? "FIX gasto mal clasificado" : "FIX reclasificación",
                partidas = new[] { Partida(e, cargo, 800m, 0m), Partida(e, abono, 0m, 800m) }, reclasificaPolizaId = reclasifica,
            };
            var borrador = await Json(await e.Analista.PostAsJsonAsync(Polizas, Poliza(e.Gasto, e.Banco)));

            // Un borrador no se reclasifica.
            var deBorrador = await e.Analista.PostAsJsonAsync(Polizas, Poliza(e.Banco, e.Gasto, Id(borrador)));
            Assert.Equal("CONTAB_POLIZA_RECLASIFICA_NO_AUTORIZADA", await Code(deBorrador));

            var original = await Json(await Send(e.Supervisor, HttpMethod.Post, $"{Polizas}/{Id(borrador)}/autorizar", null, Etag(borrador)));
            var r = await e.Analista.PostAsJsonAsync(Polizas, Poliza(e.Banco, e.Gasto, Id(original)));
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            var reclasificacion = await Json(r);
            Assert.Equal("Reclasificacion", reclasificacion.GetProperty("relacion").GetString());
            Assert.Equal(Id(original), reclasificacion.GetProperty("polizaRelacionadaId").GetGuid());
            Assert.Equal(original.GetProperty("folio").GetString(), reclasificacion.GetProperty("polizaRelacionadaFolio").GetString());

            var vista = await ObtenerAsync(e.Supervisor, $"{Polizas}/{Id(original)}");
            var ligada = Assert.Single(vista.GetProperty("ligadas").EnumerateArray());
            Assert.Equal(Id(reclasificacion), Id(ligada));
            Assert.Equal("Reclasificacion", ligada.GetProperty("relacion").GetString());
            Assert.Equal("Borrador", ligada.GetProperty("estado").GetString());
        }
        finally { await LimpiarAsync(e); }
    }
}
