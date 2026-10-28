using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.ActivosFijos.Infrastructure.Persistence.Migrations;
using Millet.Api.IntegrationTests.ComercioExterior;
using Millet.Api.Seed;
using Millet.CentrosCosto.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.ActivosFijos;

/// <summary>Preparadas para Claude: PostgreSQL real, sin ejecutar desde Codex.</summary>
public sealed class TrasladoDirectoP15HttpTests
{
    private const string Base = "/api/v1/activos-fijos/activos";
    private static async Task<(Guid Id, CustodiaActivo Custodia)> CrearAsync(ExpedientesHttpTests.Escenario e)
    {
        using var scope = e.Scope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var centros = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();
        var centro = new Dim1(Guid.NewGuid(), "P15" + Guid.NewGuid().ToString("N")[..6], "Centro sintético P15");
        centros.Dim1s.Add(centro); await centros.SaveChangesAsync();
        var db = scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
        var clase = await db.Clases.SingleAsync(x => x.Clave == "154" && x.EmpresaId == DemoSesionSeedHostedService.EmpresaId);
        var custodia = new CustodiaActivo(e.Responsable, e.SucursalId, "Origen sintético P15", centro.Id, null);
        var a = new Activo(Guid.NewGuid(), DemoSesionSeedHostedService.EmpresaId, "P15-" + Guid.NewGuid().ToString("N"),
            new("Activo sintético P15", clase.Id, null, null, null, new(2026, 1, 1), new(2026, 1, 15),
                1000, "MXN", 1, custodia, TipoImportacionActivo.No, null, false, "Soporte sintético P15"));
        a.Capitalizar(Guid.NewGuid()); db.Activos.Add(a); await db.SaveChangesAsync();
        return (a.Id, custodia);
    }
    private static async Task<HttpResponseMessage> Post(HttpClient c, Guid id, object body, string? version, string accion = "trasladar")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{id}/{accion}") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.TryAddWithoutValidation("If-Match", version ?? "\"1\"");
        return await c.SendAsync(request);
    }

    [Fact]
    public async Task Con_permiso_aplica_dos_traslados_y_detalle_expone_historial_con_nombres()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var c = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        var (id, origen) = await CrearAsync(e);
        foreach (var ubicacion in new[] { "Destino B", "Destino C" })
        {
            using var antes = await c.GetAsync($"{Base}/{id}"); antes.EnsureSuccessStatusCode();
            using var respuesta = await Post(c, id, new { destino = origen with { Ubicacion = ubicacion }, motivo = "Reubicación sintética", fechaEfectiva = "2026-02-15" }, antes.Headers.ETag!.Tag);
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        }
        using var detalle = JsonDocument.Parse(await c.GetStringAsync($"{Base}/{id}"));
        var a = detalle.RootElement;
        Assert.Equal("Destino C", a.GetProperty("ubicacion").GetString());
        Assert.False(a.GetProperty("enTraslado").GetBoolean());
        Assert.False(a.TryGetProperty("puedeAceptarTraslado", out _));
        Assert.Equal(2, a.GetProperty("traslados").GetArrayLength());
        foreach (var t in a.GetProperty("traslados").EnumerateArray())
        {
            Assert.Equal("2026-02-15", t.GetProperty("fechaEfectiva").GetString());
            Assert.Equal("Reubicación sintética", t.GetProperty("motivo").GetString());
            Assert.Equal(t.GetProperty("registradoEn").GetString(), t.GetProperty("aceptadoEn").GetString());
            Assert.False(string.IsNullOrWhiteSpace(t.GetProperty("registradoPorNombre").GetString()));
            foreach (var campo in new[] { "origenDescripcion", "destinoDescripcion" })
                foreach (var nombre in new[] { "sucursal", "responsable", "centroCosto" })
                    Assert.False(string.IsNullOrWhiteSpace(t.GetProperty(campo).GetProperty(nombre).GetString()));
        }
    }

    [Fact]
    public async Task Sin_permiso_o_sin_alcance_de_origen_o_destino_rechaza_403()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        var (id, origen) = await CrearAsync(e);
        using var propio = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        using var ninguno = await e.ClienteAsync(false, permisosCe: false);
        using var ajeno = await e.ClienteAsync(true, asociar: false, permisosCe: false, permisosAf: true);
        using var detalle = await propio.GetAsync($"{Base}/{id}");
        foreach (var c in new[] { ninguno, ajeno })
        {
            using var respuesta = await Post(c, id, new { destino = origen with { Ubicacion = "B" }, motivo = "Prueba", fechaEfectiva = "2026-02-15" }, detalle.Headers.ETag!.Tag);
            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        }
        Guid otraSucursal;
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            otraSucursal = await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Sucursales
                .Where(x => x.EmpresaId == DemoSesionSeedHostedService.EmpresaId && x.Clave == "MTY").Select(x => x.Id).SingleAsync();
        }
        using var destinoAjeno = await Post(propio, id, new { destino = origen with { SucursalId = otraSucursal }, motivo = "Prueba", fechaEfectiva = "2026-02-15" }, detalle.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.Forbidden, destinoAjeno.StatusCode);
        using var despues = JsonDocument.Parse(await propio.GetStringAsync($"{Base}/{id}"));
        Assert.Equal(origen.Ubicacion, despues.RootElement.GetProperty("ubicacion").GetString());
        Assert.Empty(despues.RootElement.GetProperty("traslados").EnumerateArray());
    }

    [Fact]
    public async Task Ruta_aceptar_ya_no_existe()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var c = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        using var respuesta = await Post(c, Guid.NewGuid(), new { trasladoId = Guid.NewGuid() }, null, "aceptar");
        Assert.Contains(respuesta.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.BadRequest });
    }

    [Fact]
    public async Task Sql_de_migracion_aplica_pendiente_y_es_seguro_al_repetir_con_cero()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        var (id, origen) = await CrearAsync(e);
        using var scope = e.Scope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
        var a = await db.Activos.Include(x => x.Traslados).SingleAsync(x => x.Id == id);
        var destino = origen with { Ubicacion = "Pendiente histórico sintético" };
        var ahora = new DateTimeOffset(2026, 2, 15, 12, 0, 0, TimeSpan.Zero);
        a.Trasladar(Guid.NewGuid(), destino, "Histórico sintético", new(2026, 2, 15), e.Responsable, ahora);
        await db.SaveChangesAsync();
        // Simula el estado persistido por el flujo anterior.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE activos_fijos.activos SET ubicacion = {origen.Ubicacion} WHERE id = {id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE activos_fijos.traslados SET aceptado_en = NULL WHERE activo_id = {id}");
        await db.Database.ExecuteSqlRawAsync(P15TrasladoDirecto.AplicarPendientesSql);
        db.ChangeTracker.Clear();
        a = await db.Activos.Include(x => x.Traslados).SingleAsync(x => x.Id == id);
        Assert.Equal(destino, a.Custodia()); Assert.False(a.EnTraslado);
        Assert.Equal(ahora, Assert.Single(a.Traslados).AceptadoEn);
        Assert.Equal(0, await db.Database.ExecuteSqlRawAsync(P15TrasladoDirecto.AplicarPendientesSql));
    }
}
