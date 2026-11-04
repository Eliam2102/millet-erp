using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Ports;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Fixtures;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// F1-ALM-13-a, lado Contabilidad, contra el API y Postgres reales: Almacén lee por su puerto la cuenta del rol INVENTARIO de la
/// sucursal (la misma que usa el motor) y su saldo al corte solo con las partidas de esa sucursal. El endpoint del cuadre responde
/// con su forma de reporte. Año lejano propio y cuentas FIX; se limpia al terminar.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class CuentaInventarioAlm13Tests(WebApplicationFactory<Program> factory)
{
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    [Fact]
    public async Task Almacen_lee_la_cuenta_INVENTARIO_de_la_sucursal_y_su_saldo_al_corte()
    {
        var suf = Sufijo();
        var anio = Random.Shared.Next(2100, 2990);
        await using var f = factory.ConEmpresa(EmpresaBootstrapId);
        var admin = await LoginAsync(f);
        Guid? tipo = null, sucursal = null, otra = null;
        try
        {
            await AsegurarPeriodosAbiertosAsync(admin, new DateOnly(anio, 10, 1), new DateOnly(anio, 11, 1));
            sucursal = Id(await Json(await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = $"IA{suf}", Nombre = $"FIX Sucursal {suf}" })));
            otra = Id(await Json(await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = $"IB{suf}", Nombre = $"FIX Otra {suf}" })));
            var activo = await CrearCuenta(admin, Codigo(suf, "1"), "FIX activo");
            var inventario = Id(await CrearCuenta(admin, Codigo(suf, "1.1"), "FIX inventario MP vidrio", Id(activo)));
            var pasivo = await CrearCuenta(admin, Codigo(suf, "2"), "FIX pasivo", naturaleza: "Acreedora");
            var proveedores = Id(await CrearCuenta(admin, Codigo(suf, "2.1"), "FIX proveedores", Id(pasivo), naturaleza: "Acreedora"));
            var clave = $"FIX-{suf}";
            var t = await admin.PostAsJsonAsync($"{Base}/tipos-documento", new { clave, nombre = $"FIX Inventario {suf}", esPrueba = true, tipoSat = "Diario" });
            Assert.Equal(HttpStatusCode.Created, t.StatusCode);
            tipo = Id(await Json(t));
            var rol = await admin.PostAsJsonAsync($"{Base}/motor/roles", new
            {
                Codigo = "INVENTARIO", Descripcion = "FIX inventario de la sucursal", CuentaId = inventario, SucursalId = sucursal,
                Moneda = (string?)null, VigenteDesde = new DateOnly(anio, 1, 1),
            });
            Assert.Equal(HttpStatusCode.Created, rol.StatusCode);

            // Compras a $900 (oct) y a $1,000 (nov) en la sucursal; una partida de otra sucursal en la misma cuenta no cuenta.
            await PolizaAsync(f, clave, new(anio, 10, 10), sucursal.Value, inventario, proveedores, 9_000m);
            await PolizaAsync(f, clave, new(anio, 11, 10), sucursal.Value, inventario, proveedores, 10_000m);
            await PolizaAsync(f, clave, new(anio, 11, 12), otra.Value, inventario, proveedores, 777m);

            Sobre.Value = EmpresaBootstrapId;
            using (var scope = f.Services.CreateScope())
            {
                var puerto = scope.ServiceProvider.GetRequiredService<IInventarioContableReadPort>();
                var octubre = await puerto.ObtenerAsync(new DateOnly(anio, 10, 31), [sucursal.Value], default);
                Assert.Equal((inventario, 9_000m), (octubre[sucursal.Value].CuentaId!.Value, octubre[sucursal.Value].Saldo!.Value));
                var noviembre = await puerto.ObtenerAsync(new DateOnly(anio, 11, 30), [sucursal.Value], default);
                Assert.Equal(19_000m, noviembre[sucursal.Value].Saldo);
                Assert.Equal(Codigo(suf, "1.1"), noviembre[sucursal.Value].CuentaCodigo);
            }

            var reporte = await admin.GetAsync($"/api/v1/almacen/reportes/cuadre-inventario?anio={anio}&mes=11");
            Assert.Equal(HttpStatusCode.OK, reporte.StatusCode);
            var json = await Json(reporte);
            Assert.Equal("CUADRE-INVENTARIO-CONTABILIDAD", json.GetProperty("titulo").GetString());
            Assert.Equal($"{anio}-11-30", json.GetProperty("filtrosAplicados").GetProperty("corte").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/v1/almacen/reportes/cuadre-inventario?anio={anio}&mes=13")).StatusCode);
        }
        finally
        {
            Sobre.Value = null;
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
            if (sucursal is { } s)
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.roles_cuenta_cambios WHERE rol_cuenta_id IN (SELECT id FROM contabilidad.roles_cuenta WHERE sucursal_id = {0})", s);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.roles_cuenta WHERE sucursal_id = {0}", s);
            }
            if (tipo is { } tp)
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE consumidor = 'POLIZA' AND referencia IN (SELECT id::text FROM contabilidad.polizas WHERE tipo_operacion_id = {0})", tp);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.polizas WHERE tipo_operacion_id = {0}", tp);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.consecutivos_poliza WHERE tipo_operacion_id = {0}", tp);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.tipos_documento_contable WHERE id = {0}", tp);
            }
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cuentas_contables_uso WHERE cuenta_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", $"FIX-{suf}-%");
            await Limpiar(factory.Services, suf);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.periodos_contables WHERE anio = {0}", anio);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.ejercicios_contables WHERE anio = {0}", anio);
        }
    }

    private static async Task PolizaAsync(WebApplicationFactory<Program> f, string clave, DateOnly fecha, Guid sucursal, Guid cargo, Guid abono, decimal importe)
    {
        Sobre.Value = EmpresaBootstrapId;
        using var scope = f.Services.CreateScope();
        var documento = Guid.NewGuid();
        await scope.ServiceProvider.GetRequiredService<IPolizaContablePort>().RegistrarAutomaticaAsync(new SolicitudPolizaAutomatica(
            clave, fecha, "FIX recepción ALM-13", new DocumentoOrigenPoliza("almacen", "recepcion_oc", documento, null, null),
            $"fix.alm13:{documento}", [new PartidaRequest(cargo, null, importe, 0m, sucursal), new PartidaRequest(abono, null, 0m, importe, sucursal)]),
            default);
    }
}
