using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// F1-CON-03 (C1.1): crear ejercicio, cerrar y reabrir con historial, permisos, concurrencia y el puerto público
/// <c>IPeriodoContableReadPort</c>. HTTP real + Postgres real. Cada prueba usa un ejercicio propio (2040–2099) y lo borra al final.
/// </summary>
public class PeriodosHttpTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string P = Base + "/periodos";
    private static int _siguiente = Random.Shared.Next(0, 50);

    private static int EjercicioDePrueba() => 2040 + Interlocked.Increment(ref _siguiente) % 60;

    private static async Task<JsonElement[]> CrearEjercicio(HttpClient c, int ejercicio)
    {
        var r = await c.PostAsJsonAsync($"{P}/ejercicios", new { ejercicio });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await Json(r)).GetProperty("periodos").EnumerateArray().ToArray();
    }

    private static async Task Borrar(IServiceProvider sp, int ejercicio)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM contabilidad.periodos_contables_eventos e USING contabilidad.periodos_contables p " +
            "WHERE e.periodo_id = p.id AND p.ejercicio = {0}; DELETE FROM contabilidad.periodos_contables WHERE ejercicio = {0};", ejercicio);
    }

    private static async Task<JsonElement> Accion(HttpClient c, JsonElement periodo, string accion, object body, HttpStatusCode esperado = HttpStatusCode.OK)
    {
        var r = await Send(c, HttpMethod.Post, $"{P}/{periodo.GetProperty("id").GetGuid()}/{accion}", body, Etag(periodo));
        Assert.Equal(esperado, r.StatusCode);
        return await Json(r);
    }

    [Fact]
    public async Task Crear_ejercicio_da_13_periodos_abiertos_y_repetirlo_es_409()
    {
        var ej = EjercicioDePrueba();
        try
        {
            var c = await LoginAsync(factory);
            var periodos = await CrearEjercicio(c, ej);
            Assert.Equal(13, periodos.Length);
            Assert.All(periodos, p => Assert.Equal("Abierto", p.GetProperty("estado").GetString()));
            Assert.Equal($"{ej}-01-01", periodos[0].GetProperty("fechaInicio").GetString());
            Assert.Equal($"{ej}-12-31", periodos[11].GetProperty("fechaFin").GetString());
            Assert.True(periodos[12].GetProperty("esPeriodoAjustes").GetBoolean());
            Assert.Equal(JsonValueKind.Null, periodos[12].GetProperty("fechaInicio").ValueKind);

            var otra = await c.PostAsJsonAsync($"{P}/ejercicios", new { ejercicio = ej });
            Assert.Equal(HttpStatusCode.Conflict, otra.StatusCode);
            Assert.Equal("CONTAB_EJERCICIO_YA_EXISTE", await Code(otra));

            var lista = await Json(await c.GetAsync($"{P}?ejercicio={ej}"));
            Assert.Equal(13, lista.GetArrayLength());
            var ejercicios = await Json(await c.GetAsync($"{P}/ejercicios"));
            Assert.Contains(ejercicios.EnumerateArray(), e => e.GetInt32() == ej);
        }
        finally { await Borrar(factory.Services, ej); }
    }

    [Fact]
    public async Task Cerrar_y_reabrir_quedan_en_el_historial_y_el_puerto_lo_refleja()
    {
        var ej = EjercicioDePrueba();
        try
        {
            var c = await LoginAsync(factory);
            var enero = (await CrearEjercicio(c, ej))[0];
            var almacenAntes = await Escalar<long>(factory.Services, "SELECT count(*) FROM almacen.periodos_cerrados");

            var cerrado = await Accion(c, enero, "cerrar", new { motivo = "Cierre de enero" });
            Assert.Equal("Cerrado", cerrado.GetProperty("estado").GetString());
            Assert.False(string.IsNullOrEmpty(cerrado.GetProperty("cerradoPor").GetString()));

            using (var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId))
            using (var scope = enEmpresa.Services.CreateScope())
            {
                var puerto = scope.ServiceProvider.GetRequiredService<IPeriodoContableReadPort>();
                Assert.False(await puerto.EstaAbiertoAsync(ej, 1, default));
                Assert.True(await puerto.EstaAbiertoAsync(ej, 2, default));
            }

            // Reabrir sin motivo: 400 (validación); con motivo: abierto y auditado (CA10.10, parte de periodos).
            var sinMotivo = await Send(c, HttpMethod.Post, $"{P}/{enero.GetProperty("id").GetGuid()}/reabrir", new { motivo = "" }, Etag(cerrado));
            Assert.Equal(HttpStatusCode.BadRequest, sinMotivo.StatusCode);
            var reabierto = await Accion(c, cerrado, "reabrir", new { motivo = "Ajuste de provisión de enero" });
            Assert.Equal("Abierto", reabierto.GetProperty("estado").GetString());
            Assert.False(string.IsNullOrEmpty(reabierto.GetProperty("reabiertoPor").GetString()));

            var historial = (await Json(await c.GetAsync($"{P}/{enero.GetProperty("id").GetGuid()}/historial"))).EnumerateArray().ToArray();
            Assert.Equal(["Reabierto", "Cerrado", "Creado"], historial.Select(h => h.GetProperty("accion").GetString()));
            Assert.Equal("Ajuste de provisión de enero", historial[0].GetProperty("motivo").GetString());

            // C1.1-b (D18): reabrir contabilidad no toca el cierre de inventario de Almacén.
            Assert.Equal(almacenAntes, await Escalar<long>(factory.Services, "SELECT count(*) FROM almacen.periodos_cerrados"));
        }
        finally { await Borrar(factory.Services, ej); }
    }

    [Fact]
    public async Task Version_desactualizada_es_409_y_sin_If_Match_es_428()
    {
        var ej = EjercicioDePrueba();
        try
        {
            var c = await LoginAsync(factory);
            var enero = (await CrearEjercicio(c, ej))[0];
            await Accion(c, enero, "cerrar", new { motivo = (string?)null });
            // `enero` trae la versión anterior: otro usuario ya lo cerró.
            var vieja = await Send(c, HttpMethod.Post, $"{P}/{enero.GetProperty("id").GetGuid()}/reabrir", new { motivo = "x" }, Etag(enero));
            Assert.Equal(HttpStatusCode.Conflict, vieja.StatusCode);
            var sinIfMatch = await Send(c, HttpMethod.Post, $"{P}/{enero.GetProperty("id").GetGuid()}/reabrir", new { motivo = "x" });
            Assert.Equal(HttpStatusCode.PreconditionRequired, sinIfMatch.StatusCode);
        }
        finally { await Borrar(factory.Services, ej); }
    }

    [Fact]
    public async Task Sin_permiso_de_reabrir_no_puede_reabrir()
    {
        var ej = EjercicioDePrueba();
        try
        {
            var admin = await LoginAsync(factory);
            var enero = (await CrearEjercicio(admin, ej))[0];
            var cerrado = await Accion(admin, enero, "cerrar", new { motivo = (string?)null });

            // C1.1-a: puede ver y cerrar, pero no reabrir (eso es del Contador General).
            var c = await ClienteConPermisosAsync(factory, PermisosCanonicos.ContabilidadPeriodoLeer, PermisosCanonicos.ContabilidadPeriodoCerrar);
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"{P}?ejercicio={ej}")).StatusCode);
            var r = await Send(c, HttpMethod.Post, $"{P}/{cerrado.GetProperty("id").GetGuid()}/reabrir", new { motivo = "x" }, Etag(cerrado));
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            var crear = await c.PostAsJsonAsync($"{P}/ejercicios", new { ejercicio = ej + 1 });
            Assert.Equal(HttpStatusCode.Forbidden, crear.StatusCode);
        }
        finally { await Borrar(factory.Services, ej); }
    }

    [Fact]
    public async Task Puerto_periodo_13_solo_ajustes_manuales_despues_de_cerrar_diciembre()
    {
        var ej = EjercicioDePrueba();
        try
        {
            var c = await LoginAsync(factory);
            var periodos = await CrearEjercicio(c, ej);
            using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
            using var scope = enEmpresa.Services.CreateScope();
            var puerto = scope.ServiceProvider.GetRequiredService<IPeriodoContableReadPort>();

            Assert.Equal(MotivoRechazoPeriodo.NoExiste, (await puerto.ValidarRegistroAsync(2199, 1, false, default)).Motivo);
            Assert.False(await puerto.EstaAbiertoAsync(2199, 1, default));
            Assert.False(await puerto.EstaAbiertoAsync(ej, 13, default));

            // CA10.12 (parte de periodos): el 13 rechaza lo automático y, antes de cerrar diciembre, también lo manual.
            Assert.Equal(MotivoRechazoPeriodo.Periodo13SoloAjusteAuditoria, (await puerto.ValidarRegistroAsync(ej, 13, false, default)).Motivo);
            Assert.Equal(MotivoRechazoPeriodo.Periodo13AntesDelCierreDeDiciembre, (await puerto.ValidarRegistroAsync(ej, 13, true, default)).Motivo);

            await Accion(c, periodos[11], "cerrar", new { motivo = "Cierre ordinario" });
            Assert.True((await puerto.ValidarRegistroAsync(ej, 13, true, default)).Valido);
            Assert.Equal(MotivoRechazoPeriodo.Cerrado, (await puerto.ValidarRegistroAsync(ej, 12, true, default)).Motivo);
            Assert.True((await puerto.ValidarRegistroAsync(ej, 3, false, default)).Valido);
        }
        finally { await Borrar(factory.Services, ej); }
    }
}
