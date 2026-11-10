using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Application.ContabilidadElectronica;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// K10.8 (F1-CON-13) contra el API y Postgres reales: revisión previa con las cuentas sin código agrupador (K10.8-a), el catálogo
/// bloqueado mientras falten, la balanza del mes en .zip con el nombre del SAT y validada contra su esquema, y las pólizas en XML.
/// El RFC de la empresa se fija con un puerto de prueba para que el nombre del archivo no dependa de la configuración del ambiente.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class ContabilidadElectronicaHttpTests(WebApplicationFactory<Program> factory)
{
    private const string Rfc = "AAA010101AAA";
    private const string Ce = Base + "/contabilidad-electronica";

    private static readonly string[] PermisosAnalista =
        [PermisosCanonicos.ContabilidadPolizasLeer, PermisosCanonicos.ContabilidadPolizasCapturar, PermisosCanonicos.ContabilidadMovimientosGestionarTodasSucursales];

    private static readonly string[] PermisosSupervisor =
        [PermisosCanonicos.ContabilidadPolizasLeer, PermisosCanonicos.ContabilidadPolizasAutorizar, PermisosCanonicos.ContabilidadMovimientosGestionarTodasSucursales];

    private sealed class RfcFijo : IEmpresaContabilidadReadPort
    {
        public Task<string?> RfcAsync(Guid empresaId, CancellationToken ct) => Task.FromResult<string?>(Rfc);
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    [Fact]
    public async Task K10_8_revision_lista_las_cuentas_sin_codigo_agrupador_y_la_balanza_y_las_polizas_salen_validas_para_el_SAT()
    {
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IEmpresaContabilidadReadPort, RfcFijo>()));
        var hoy = Millet.SharedKernel.Application.FechaContable.HoyLocal(DateTimeOffset.UtcNow);
        var anterior = hoy.AddMonths(-1);
        var suf = Sufijo();
        HttpClient? analista = null, supervisor = null;
        Guid? tipo = null;
        try
        {
            var admin = await LoginAsync(app);
            await AsegurarPeriodosAbiertosAsync(admin, anterior, hoy);
            var sucursal = await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = $"CE{suf}", Nombre = $"FIX Sucursal {suf}" });
            sucursal.EnsureSuccessStatusCode();
            var sucursalId = Id(await Json(sucursal));
            var raiz = await CrearCuenta(admin, Codigo(suf, "6"), "FIX gastos");
            var gasto = await CrearCuenta(admin, Codigo(suf, "6.1"), "FIX gasto", Id(raiz));
            var banco = await CrearCuenta(admin, Codigo(suf, "6.2"), "FIX bancos", Id(raiz));
            var t = await admin.PostAsJsonAsync($"{Base}/tipos-documento", new { clave = $"FIX-{suf}", nombre = $"FIX Diario {suf}", esPrueba = true, tipoSat = "Diario" });
            Assert.Equal(HttpStatusCode.Created, t.StatusCode);
            tipo = Id(await Json(t));
            analista = await ClienteConPermisosAsync(app, PermisosAnalista);
            supervisor = await ClienteConPermisosAsync(app, PermisosSupervisor);

            async Task AutorizadaAsync(DateOnly fecha, decimal importe)
            {
                var partidas = new object[]
                {
                    new { cuentaId = Id(gasto), concepto = $"FIX {suf}", cargo = importe, abono = 0m, sucursalId },
                    new { cuentaId = Id(banco), concepto = $"FIX {suf}", cargo = 0m, abono = importe, sucursalId },
                };
                var r = await analista.PostAsJsonAsync($"{Base}/polizas", new { tipoOperacionId = tipo, fecha, concepto = $"FIX CE {suf}", partidas });
                Assert.Equal(HttpStatusCode.Created, r.StatusCode);
                var p = await Json(r);
                Assert.Equal(HttpStatusCode.OK, (await Send(supervisor, HttpMethod.Post, $"{Base}/polizas/{Id(p)}/autorizar", null, Etag(p))).StatusCode);
            }
            await AutorizadaAsync(anterior, 1_000m);
            await AutorizadaAsync(hoy, 300m);
            var filtro = $"anio={hoy.Year}&mes={hoy.Month}";

            // ── Revisión (K10.8-a): las cuentas FIX no tienen código agrupador y se listan; el catálogo queda bloqueado ──
            var rev = await supervisor.GetAsync($"{Ce}/revision?{filtro}");
            Assert.Equal(HttpStatusCode.OK, rev.StatusCode);
            var revision = await Json(rev);
            Assert.Equal(Rfc, revision.GetProperty("rfc").GetString());
            Assert.False(revision.GetProperty("puedeGenerarCatalogo").GetBoolean());
            var problemas = revision.GetProperty("problemas").EnumerateArray().ToList();
            Assert.Contains(problemas, p => p.GetProperty("cuentaId").GetGuid() == Id(gasto)
                && p.GetProperty("problema").GetString() == GeneradorContabilidadElectronica.SinCodigoAgrupador);
            Assert.True(revision.GetProperty("polizas").GetInt32() >= 1);

            var catalogo = await supervisor.GetAsync($"{Ce}/catalogo?{filtro}");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, catalogo.StatusCode);
            Assert.Equal("CONTAB_CE_CATALOGO_INCOMPLETO", await Code(catalogo));

            // ── Balanza: .zip con el nombre del SAT, XML válido; la cuenta de mayor acumula a sus subcuentas ──
            var bal = await supervisor.GetAsync($"{Ce}/balanza?{filtro}");
            Assert.Equal(HttpStatusCode.OK, bal.StatusCode);
            Assert.Equal("application/zip", bal.Content.Headers.ContentType?.MediaType);
            var nombre = $"{Rfc}{hoy.Year:D4}{hoy.Month:D2}BN";
            Assert.Equal($"{nombre}.zip", bal.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
            using var zip = new ZipArchive(new MemoryStream(await bal.Content.ReadAsByteArrayAsync()));
            var entrada = Assert.Single(zip.Entries);
            Assert.Equal($"{nombre}.xml", entrada.FullName);
            XDocument xml;
            await using (var s = entrada.Open()) xml = XDocument.Load(s);
            Assert.Empty(EsquemasSat.Validar(xml));
            string[] Valores(string codigo)
            {
                var c = xml.Root!.Elements(XName.Get("Ctas", EsquemasSat.NsBalanza)).Single(x => (string)x.Attribute("NumCta")! == codigo);
                return [(string)c.Attribute("SaldoIni")!, (string)c.Attribute("Debe")!, (string)c.Attribute("Haber")!, (string)c.Attribute("SaldoFin")!];
            }
            Assert.Equal(["1000.00", "300.00", "0.00", "1300.00"], Valores(Codigo(suf, "6.1")));
            Assert.Equal(["-1000.00", "0.00", "300.00", "-1300.00"], Valores(Codigo(suf, "6.2")));
            Assert.Equal(["0.00", "300.00", "300.00", "0.00"], Valores(Codigo(suf, "6")));

            var sinFecha = await supervisor.GetAsync($"{Ce}/balanza?{filtro}&tipoEnvio=C");
            Assert.Equal(HttpStatusCode.BadRequest, sinFecha.StatusCode);

            // ── Pólizas para una devolución, en XML sin comprimir ──
            var pl = await supervisor.GetAsync($"{Ce}/polizas?{filtro}&solicitud=DE&numTramite=AB123456789012&formato=xml");
            Assert.Equal(HttpStatusCode.OK, pl.StatusCode);
            var polizas = XDocument.Parse(await pl.Content.ReadAsStringAsync());
            Assert.Empty(EsquemasSat.Validar(polizas));
            Assert.Equal("AB123456789012", (string)polizas.Root!.Attribute("NumTramite")!);
            var nuestra = Assert.Single(polizas.Root.Elements(XName.Get("Poliza", EsquemasSat.NsPolizas)),
                p => (string)p.Attribute("Concepto")! == $"FIX CE {suf}");
            Assert.Equal(2, nuestra.Elements(XName.Get("Transaccion", EsquemasSat.NsPolizas)).Count());
        }
        finally
        {
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                if (tipo is { } tipoId)
                {
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE consumidor = 'POLIZA' AND referencia IN (SELECT id::text FROM contabilidad.polizas WHERE tipo_operacion_id = {0})", tipoId);
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.polizas WHERE tipo_operacion_id = {0}", tipoId);
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.consecutivos_poliza WHERE tipo_operacion_id = {0}", tipoId);
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.tipos_documento_contable WHERE id = {0}", tipoId);
                }
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cuentas_contables_uso WHERE cuenta_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", $"FIX-{suf}-%");
            }
            await Limpiar(app.Services, suf);
            if (analista is not null) await LimpiarClienteAsync(analista);
            if (supervisor is not null) await LimpiarClienteAsync(supervisor);
        }
    }
}
