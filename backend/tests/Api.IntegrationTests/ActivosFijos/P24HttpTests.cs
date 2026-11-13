using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.ComercioExterior;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.ActivosFijos;

public sealed partial class LibroHttpTests
{
    [Fact]
    public async Task P24_E3g_dos_bajas_simultaneas_solo_una_pendiente_y_retiro_libera_candado()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        var id = await Preparar(e, false);
        using var autor = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        using var otro = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        var a = await autor.GetFromJsonAsync<JsonElement>($"{Base}/activos/{id}");
        var version = a.GetProperty("version").GetInt32();
        var datos = new { tipo = "Baja", fecha = "2027-02-01", importe = 0, motivo = "Siniestro", solicitadoPor = "Área usuaria", evidencia = "data:application/pdf;base64," + Convert.ToBase64String(Millet.Api.Seed.DemoSesionSeedHostedService.CrearPdf("Evidencia QA sintética")) };
        var respuestas = await Task.WhenAll(Post(autor, $"activos/{id}/operaciones", datos, version), Post(autor, $"activos/{id}/operaciones", datos, version));
        try
        {
            var ok = Assert.Single(respuestas, r => r.IsSuccessStatusCode);
            var rechazo = Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
            Assert.Contains("Ya hay una baja pendiente registrada el 01-feb-2027", await rechazo.Content.ReadAsStringAsync());
            var op = await ok.Content.ReadFromJsonAsync<JsonElement>();
            var ruta = $"operaciones/{op.GetProperty("id").GetGuid()}";
            var v = op.GetProperty("version").GetInt32();
            using var ajeno = await Post(otro, ruta + "/retirar", new { motivo = "Error de captura" }, v);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, ajeno.StatusCode);
            using var vacio = await Post(autor, ruta + "/retirar", new { motivo = " " }, v);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, vacio.StatusCode);
            using var retirar = await Post(autor, ruta + "/retirar", new { motivo = "Error de captura" }, v);
            retirar.EnsureSuccessStatusCode();
            var retirada = await retirar.Content.ReadFromJsonAsync<JsonElement>();
            using var cerrada = await Post(otro, ruta + "/autorizar", new { }, retirada.GetProperty("version").GetInt32());
            Assert.Equal(HttpStatusCode.UnprocessableEntity, cerrada.StatusCode);
            using var nueva = await Post(autor, $"activos/{id}/operaciones", datos, version);
            nueva.EnsureSuccessStatusCode();
        }
        finally { foreach (var r in respuestas) r.Dispose(); }
        var historial = await autor.GetFromJsonAsync<JsonElement>($"{Base}/activos/{id}/libro");
        Assert.Contains(historial.GetProperty("operaciones").EnumerateArray(), o => o.GetProperty("motivoRetiro").GetString() == "Error de captura");
        using var scope = e.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
        Assert.Equal(1, await db.Set<OperacionLibro>().CountAsync(o => o.ActivoId == id && !o.Anulada && o.PolizaId == null));
        // El índice protege también escrituras que no pasan por el candado de la aplicación.
        db.Add(new OperacionLibro { EmpresaId = (await db.Activos.SingleAsync(a => a.Id == id)).EmpresaId, ActivoId = id, Tipo = "Baja", Fecha = new(2027,2,1), SolicitadoPor = "Prueba" });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("ux_operacion_baja_pendiente", Assert.IsType<Npgsql.PostgresException>(ex.InnerException).ConstraintName);
    }

    [Fact]
    public async Task P24_migracion_retira_duplicadas_conservando_la_mas_reciente()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        var id = await Preparar(e, false);
        using var scope = e.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
        await db.Database.ExecuteSqlRawAsync("DROP INDEX activos_fijos.ux_operacion_baja_pendiente");
        var empresa = (await db.Activos.SingleAsync(a => a.Id == id)).EmpresaId;
        var antigua = new OperacionLibro { EmpresaId = empresa, ActivoId = id, Tipo = "Baja", Fecha = new(2027,2,1), SolicitadoPor = "Área usuaria" };
        var reciente = new OperacionLibro { EmpresaId = empresa, ActivoId = id, Tipo = "Baja", Fecha = new(2027,3,1), SolicitadoPor = "Área usuaria" };
        db.Add(antigua); await db.SaveChangesAsync();
        db.Add(reciente); await db.SaveChangesAsync();
        var migracion = new Millet.ActivosFijos.Infrastructure.Persistence.Migrations.P24BrechasAuditoriaActivos();
        foreach (var sql in migracion.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
            await db.Database.ExecuteSqlRawAsync(sql.Sql);
        db.ChangeTracker.Clear();
        var filas = await db.Set<OperacionLibro>().OrderBy(o => o.Fecha).ToListAsync();
        Assert.Equal(2, filas.Count); Assert.True(filas[0].Anulada); Assert.NotNull(filas[0].RetiradoEn);
        Assert.Equal("Duplicada al crear el candado (E-3)", filas[0].MotivoRetiro); Assert.False(filas[1].Anulada);
        var generador = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsSqlGenerator>();
        var crear = migracion.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation>().Single();
        foreach (var comando in generador.Generate([crear], db.Model)) await db.Database.ExecuteSqlRawAsync(comando.CommandText);
        Assert.Equal(1, await db.Set<OperacionLibro>().CountAsync(o => !o.Anulada));
    }

    [Theory]
    [InlineData("160", null, false)]
    [InlineData("160", 60, true)]
    [InlineData("170", null, false)]
    public async Task P24_E3a_E3b_clases_especiales_bloquean_sin_regla_y_confirman_con_vida_util(string clave, int? vida, bool confirma)
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        var id = await Preparar(e, false);
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
            var original = await db.Activos.SingleAsync(a => a.Id == id);
            var anterior = await db.Clases.SingleAsync(c => c.Id == original.ClaseId);
            var clase = await db.Clases.SingleAsync(c => c.Clave == clave && c.EmpresaId == original.EmpresaId);
            clase.Editar(clase.Nombre, null, anterior.CuentaActivoId, anterior.CuentaDepreciacionId, anterior.CuentaGastoId, anterior.CuentaPuenteId, true);
            // Reproduce una carga histórica incompleta: el alta actual valida la regla antes de capitalizar.
            db.Entry(original).Property(a => a.ClaseId).CurrentValue = clase.Id;
            db.Entry(original).Property(a => a.VidaUtilMeses).CurrentValue = vida;
            await db.SaveChangesAsync();
        }
        using var cliente = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        using var respuesta = await Post(cliente, "depreciacion", new { periodo = "2027-02-01" });
        if (confirma) respuesta.EnsureSuccessStatusCode();
        else
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
            Assert.Contains("FIX-P4-MONTACARGAS", await respuesta.Content.ReadAsStringAsync());
        }
        using var verificar = e.Scope(); using var lectura = verificar.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var af = verificar.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
        var contab = verificar.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        var polizas = await contab.Polizas.Where(p => p.OrigenModulo == "ActivosFijos").ToListAsync();
        Assert.Equal(confirma ? 1 : 0, polizas.Count);
        Assert.Equal(confirma ? 8333.33m : 0, (await af.Activos.SingleAsync(a => a.Id == id)).DepreciacionAcumulada);
        Assert.Equal(confirma ? 1 : 0, await af.Set<CorridaDepreciacion>().CountAsync());
    }

    [Fact]
    public async Task P24_E4e_reporte_fiscal_usa_tasas_por_mes_sin_generar_poliza()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        var id = await Preparar(e, false);
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
            var a = await db.Activos.SingleAsync(a => a.Id == id);
            var c = await db.Clases.Include(c => c.Versiones).SingleAsync(c => c.Id == a.ClaseId);
            c.Versiones.Add(VersionClaseActivo.Desde(c, 1, new(2000,1,1), Guid.NewGuid(), true));
            var v = VersionClaseActivo.Desde(c, 2, new(2027,6,1), Guid.NewGuid());
            v.TasaFiscalAnual = 25; v.Resolver(Guid.NewGuid(), true, null, DateTimeOffset.UtcNow); c.Versiones.Add(v);
            await db.SaveChangesAsync();
        }
        using var cliente = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        var reporte = await cliente.GetFromJsonAsync<Millet.ActivosFijos.Application.ReporteFiscal>($"{Base}/reportes/depreciacion-fiscal?ejercicio=2027&mesCorte=12");
        Assert.Equal(89583.33m, Assert.Single(reporte!.Filas, f => f.ActivoId == id).DeduccionHistorica); // 4 meses al 10 % y 7 al 25 %.
        using var verificar = e.Scope(); using var lectura = verificar.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        Assert.Empty(await verificar.ServiceProvider.GetRequiredService<ContabilidadDbContext>().Polizas.Where(p => p.OrigenModulo == "ActivosFijos").ToListAsync());
    }

    [Fact]
    public async Task P24_E4f_poliza_usa_cuenta_del_mes_y_conserva_febrero()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        var id = await Preparar(e, false);
        Guid anterior, nueva;
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
            var a = await db.Activos.SingleAsync(a => a.Id == id);
            var c = await db.Clases.Include(c => c.Versiones).SingleAsync(c => c.Id == a.ClaseId);
            anterior = c.CuentaGastoId!.Value; nueva = c.CuentaActivoId!.Value;
            c.Versiones.Add(VersionClaseActivo.Desde(c, 1, new(2000,1,1), Guid.NewGuid(), true));
            var v = VersionClaseActivo.Desde(c, 2, new(2027,3,1), Guid.NewGuid());
            v.CuentaGastoId = nueva; v.Resolver(Guid.NewGuid(), true, null, DateTimeOffset.UtcNow); c.Versiones.Add(v);
            await db.SaveChangesAsync();
        }
        using var cliente = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        foreach (var mes in new[] { "2027-02-01", "2027-03-01" })
        {
            using var respuesta = await Post(cliente, "depreciacion", new { periodo = mes }); respuesta.EnsureSuccessStatusCode();
        }
        using var verificar = e.Scope(); using var lectura = verificar.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var contab = verificar.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        var polizas = await contab.Polizas.Include(p => p.Partidas).Where(p => p.OrigenModulo == "ActivosFijos" && p.OrigenTipo == "Depreciacion").OrderBy(p => p.Fecha).ToListAsync();
        Assert.Equal(2, polizas.Count);
        Assert.Equal(anterior, Assert.Single(polizas[0].Partidas, p => p.Cargo > 0).CuentaId);
        Assert.Equal(nueva, Assert.Single(polizas[1].Partidas, p => p.Cargo > 0).CuentaId);
    }
}
