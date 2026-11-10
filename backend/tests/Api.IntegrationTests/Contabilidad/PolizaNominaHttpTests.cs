using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// K10.11 (F1-CON-13) contra el API y Postgres reales: la póliza de la nómina externa nace en borrador, cuadrada y con su lote
/// (K10.11-a); un archivo que no cuadra o con cuenta, sucursal o centro inexistente se rechaza con el detalle por renglón y no crea
/// nada (K10.11-b); repetir el lote no duplica la póliza.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class PolizaNominaHttpTests(WebApplicationFactory<Program> factory)
{
    private const string Nomina = Base + "/polizas/nomina";

    private static readonly string[] PermisosCapturista =
        [PermisosCanonicos.ContabilidadPolizasLeer, PermisosCanonicos.ContabilidadPolizasCapturar, PermisosCanonicos.ContabilidadMovimientosGestionarTodasSucursales];

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private static object Cuerpo(Guid tipo, DateOnly fecha, string referencia, string csv, decimal? totalControl = null) => new
    {
        tipoOperacionId = tipo, fecha, referencia, concepto = $"Nómina {referencia}", totalControl,
        archivo = new { archivoNombre = "nomina.csv", csvBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(csv)) },
    };

    [Fact]
    public async Task K10_11_a_y_b_la_nomina_valida_nace_cuadrada_y_la_invalida_se_rechaza_por_renglon_sin_crear_nada()
    {
        var hoy = Millet.SharedKernel.Application.FechaContable.HoyLocal(DateTimeOffset.UtcNow);
        var suf = Sufijo();
        HttpClient? capturista = null;
        Guid? tipo = null;
        try
        {
            var admin = await LoginAsync(factory);
            await AsegurarPeriodosAbiertosAsync(admin, hoy);
            var clave = $"NO{suf}";
            var sucursal = await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = clave, Nombre = $"FIX Sucursal {suf}" });
            sucursal.EnsureSuccessStatusCode();
            var raiz = await CrearCuenta(admin, Codigo(suf, "6"), "FIX nómina");
            var sueldos = await CrearCuenta(admin, Codigo(suf, "6.1"), "FIX sueldos", Id(raiz));
            var porPagar = await CrearCuenta(admin, Codigo(suf, "6.2"), "FIX nómina por pagar", Id(raiz), naturaleza: "Acreedora");
            var t = await admin.PostAsJsonAsync($"{Base}/tipos-documento", new { clave = $"FIX-{suf}", nombre = $"FIX Nómina {suf}", esPrueba = true, tipoSat = "Diario" });
            Assert.Equal(HttpStatusCode.Created, t.StatusCode);
            tipo = Id(await Json(t));
            capturista = await ClienteConPermisosAsync(factory, PermisosCapturista);
            string s1 = Codigo(suf, "6.1"), s2 = Codigo(suf, "6.2");

            // ── K10.11-b: no cuadra, cuenta inexistente, sucursal y centro inexistentes, importe no numérico ──
            var malo = $"Cuenta,Debe,Haber,Sucursal,Centro de costo,Concepto,Empleado\n" +
                       $"{s1},1000,,{clave},,Sueldo,E-001\n" +
                       $"FIX-{suf}-9.9,500,,{clave},,Sueldo,E-002\n" +
                       $"{s1},diez,,XX{suf},NOEXISTE{suf},Sueldo,E-003\n" +
                       $"{s2},,1200,{clave},,Neto a pagar,\n";
            var vista = await capturista.PostAsJsonAsync($"{Nomina}/vista-previa", Cuerpo(tipo.Value, hoy, $"FIX {suf} Q1", malo));
            Assert.Equal(HttpStatusCode.OK, vista.StatusCode);
            var v = await Json(vista);
            Assert.False(v.GetProperty("valida").GetBoolean());
            var errores = v.GetProperty("errores").EnumerateArray()
                .Select(e => (Fila: e.GetProperty("fila").ValueKind == JsonValueKind.Null ? (int?)null : e.GetProperty("fila").GetInt32(),
                    Codigo: e.GetProperty("codigo").GetString())).ToList();
            Assert.Contains((3, "NOM_CUENTA_NO_EXISTE"), errores);
            Assert.Contains((4, "NOM_IMPORTE_INVALIDO"), errores);
            Assert.Contains((4, "NOM_SUCURSAL_NO_EXISTE"), errores);
            Assert.Contains((4, "NOM_CENTRO_NO_EXISTE"), errores);
            Assert.Contains(((int?)null, "NOM_NO_CUADRA"), errores);

            var rechazo = await capturista.PostAsJsonAsync(Nomina, Cuerpo(tipo.Value, hoy, $"FIX {suf} Q1", malo));
            Assert.Equal(HttpStatusCode.BadRequest, rechazo.StatusCode);
            Assert.Contains("Fila 3", await rechazo.Content.ReadAsStringAsync());
            Assert.Equal(0, await PolizasDelTipoAsync(tipo.Value));

            // ── K10.11-a: archivo válido con total de control ──
            var bueno = $"cuenta;cargo;abono;sucursal;concepto;empleado\n" +
                        $"{s1};\"1,000.50\";;{clave};Sueldo;E-001\n" +
                        $"{s1};499.50;;{clave};Sueldo;E-002\n" +
                        $"{s2};;1500;{clave};Neto a pagar;\n";
            var ok = await capturista.PostAsJsonAsync($"{Nomina}/vista-previa", Cuerpo(tipo.Value, hoy, $"FIX {suf} Q2", bueno, 1_500m));
            var okVista = await Json(ok);
            Assert.True(okVista.GetProperty("valida").GetBoolean(), okVista.ToString());
            Assert.Equal(3, okVista.GetProperty("renglones").GetInt32());

            var creada = await capturista.PostAsJsonAsync(Nomina, Cuerpo(tipo.Value, hoy, $"FIX {suf} Q2", bueno, 1_500m));
            Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
            var poliza = await Json(creada);
            Assert.Equal("Borrador", poliza.GetProperty("estado").GetString());
            var partidas = poliza.GetProperty("partidas").EnumerateArray().ToList();
            Assert.Equal(3, partidas.Count);
            Assert.Equal(1_500m, partidas.Sum(p => p.GetProperty("cargo").GetDecimal()));
            Assert.Equal(1_500m, partidas.Sum(p => p.GetProperty("abono").GetDecimal()));
            Assert.Contains(partidas, p => p.GetProperty("concepto").GetString() == "Sueldo · Empleado E-001");

            // ── Repetir el lote no duplica la póliza ──
            var repetida = await capturista.PostAsJsonAsync(Nomina, Cuerpo(tipo.Value, hoy, $"fix {suf}  q2", bueno, 1_500m));
            Assert.Equal(HttpStatusCode.BadRequest, repetida.StatusCode);
            Assert.Contains("NOM_LOTE_DUPLICADO", await repetida.Content.ReadAsStringAsync());
            Assert.Equal(1, await PolizasDelTipoAsync(tipo.Value));
        }
        finally
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                if (tipo is { } tipoId)
                {
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE consumidor = 'POLIZA' AND referencia IN (SELECT id::text FROM contabilidad.polizas WHERE tipo_operacion_id = {0})", tipoId);
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.polizas WHERE tipo_operacion_id = {0}", tipoId);
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.tipos_documento_contable WHERE id = {0}", tipoId);
                }
            }
            await Limpiar(factory.Services, suf);
            if (capturista is not null) await LimpiarClienteAsync(capturista);
        }
    }

    private async Task<int> PolizasDelTipoAsync(Guid tipo)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        return await db.Polizas.IgnoreQueryFilters().CountAsync(p => p.TipoOperacionId == tipo);
    }
}
