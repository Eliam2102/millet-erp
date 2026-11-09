using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Application.Cierre;
using Millet.Contabilidad.Infrastructure.Persistence;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// CON-12 (86e3jvvme) contra el API y Postgres reales, con el checklist exigido: un paso obligatorio pendiente impide cerrar y el
/// mensaje dice cuál (CA10.9); con excepción autorizada (o el paso manual confirmado) el periodo cierra y la foto del cierre guarda
/// quién autorizó (CON-12-a). Año lejano: Almacén nunca cerró ese mes, así que «Inventario cerrado» queda pendiente.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class CierreChecklistHttpTests(WebApplicationFactory<Program> factory)
{
    private static readonly int[] Enero = [1];

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    [Fact]
    public async Task CA10_9_y_CON12a_sin_el_checklist_no_cierra_y_con_excepcion_queda_quien_la_autorizo()
    {
        await using var host = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Contabilidad:Cierre:ExigirChecklist"] = "true" })));
        var anio = Random.Shared.Next(2100, 2990);
        try
        {
            using var admin = await LoginAsync(host);
            var ejercicio = await Json(await admin.PostAsJsonAsync($"{Base}/periodos/ejercicios", new { anio }));
            ejercicio = await Json(await Send(admin, HttpMethod.Post, $"{Base}/periodos/ejercicios/{Id(ejercicio)}/abrir", new { numeros = Enero }, Etag(ejercicio)));
            var enero = ejercicio.GetProperty("periodos").EnumerateArray().Single(p => p.GetProperty("numero").GetInt32() == 1);

            var checklist = await Json(await admin.GetAsync($"{Base}/cierre/periodos/{Id(enero)}/checklist"));
            Assert.False(checklist.GetProperty("puedeCerrar").GetBoolean());
            var inventario = checklist.GetProperty("pasos").EnumerateArray().Single(p => p.GetProperty("clave").GetString() == PasosCierre.InventarioCerrado);
            Assert.Equal("Pendiente", inventario.GetProperty("estado").GetString());
            Assert.True(inventario.GetProperty("bloquea").GetBoolean());

            // CA10.9: no cierra y dice qué falta.
            var rechazo = await Send(admin, HttpMethod.Post, $"{Base}/periodos/{Id(enero)}/cerrar", new { motivo = "FIX cierre de enero con checklist" }, Etag(enero));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rechazo.StatusCode);
            var problema = await Json(rechazo);
            Assert.Equal("CONTAB_CIERRE_CHECKLIST_INCOMPLETO", problema.GetProperty("code").GetString());
            Assert.Contains("Inventario cerrado", problema.GetProperty("detail").GetString());

            // Un paso automático no se «confirma» a mano.
            var confirmarAutomatico = await Send(admin, HttpMethod.Post, $"{Base}/cierre/periodos/{Id(enero)}/pasos/{PasosCierre.InventarioCerrado}/confirmacion",
                new { motivo = "FIX lo confirmo a mano" });
            Assert.Equal("CONTAB_CIERRE_PASO_AUTOMATICO", await Code(confirmarAutomatico));

            // CON-12-a: excepción en los automáticos pendientes y confirmación en los manuales.
            foreach (var paso in checklist.GetProperty("pasos").EnumerateArray().Where(p => p.GetProperty("bloquea").GetBoolean()))
            {
                var tipo = paso.GetProperty("tipo").GetString() == "Manual" ? "confirmacion" : "excepcion";
                (await Send(admin, HttpMethod.Post, $"{Base}/cierre/periodos/{Id(enero)}/pasos/{paso.GetProperty("clave").GetString()}/{tipo}",
                    new { motivo = "FIX Almacén cierra enero la próxima semana" })).EnsureSuccessStatusCode();
            }
            var repetida = await Send(admin, HttpMethod.Post, $"{Base}/cierre/periodos/{Id(enero)}/pasos/{PasosCierre.InventarioCerrado}/excepcion",
                new { motivo = "FIX otra excepción del mismo paso" });
            Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);

            checklist = await Json(await admin.GetAsync($"{Base}/cierre/periodos/{Id(enero)}/checklist"));
            Assert.True(checklist.GetProperty("puedeCerrar").GetBoolean());
            var cerrado = await Send(admin, HttpMethod.Post, $"{Base}/periodos/{Id(enero)}/cerrar", new { motivo = "FIX cierre de enero con checklist" }, Etag(enero));
            cerrado.EnsureSuccessStatusCode();

            checklist = await Json(await admin.GetAsync($"{Base}/cierre/periodos/{Id(enero)}/checklist"));
            var cierre = checklist.GetProperty("cierres").EnumerateArray().Single();
            Assert.True(cierre.GetProperty("excepciones").GetInt32() >= 1);
            var foto = await Json(await admin.GetAsync($"{Base}/cierre/registros/{Id(cierre)}"));
            var conExcepcion = foto.GetProperty("pasos").EnumerateArray().Single(p => p.GetProperty("clave").GetString() == PasosCierre.InventarioCerrado);
            Assert.Equal("Excepcion", conExcepcion.GetProperty("estado").GetString());
            Assert.Equal("dev-superadmin", conExcepcion.GetProperty("marca").GetProperty("usuarioNombre").GetString());
            Assert.Contains("Almacén cierra enero", conExcepcion.GetProperty("marca").GetProperty("motivo").GetString());

            // Con el periodo cerrado el checklist ya no se cambia.
            var tarde = await Send(admin, HttpMethod.Post, $"{Base}/cierre/periodos/{Id(enero)}/pasos/{PasosCierre.Depreciacion}/excepcion",
                new { motivo = "FIX excepción después del cierre" });
            Assert.Equal("CONTAB_CIERRE_PERIODO_CERRADO", await Code(tarde));
        }
        finally
        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
            const string periodos = "SELECT id FROM contabilidad.periodos_contables WHERE anio = {0}";
            await db.Database.ExecuteSqlRawAsync($"DELETE FROM contabilidad.marcas_paso_cierre WHERE periodo_id IN ({periodos})", anio);
            await db.Database.ExecuteSqlRawAsync($"DELETE FROM contabilidad.cierres_periodo WHERE periodo_id IN ({periodos})", anio);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.periodos_contables WHERE anio = {0}", anio);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.ejercicios_contables WHERE anio = {0}", anio);
        }
    }
}
