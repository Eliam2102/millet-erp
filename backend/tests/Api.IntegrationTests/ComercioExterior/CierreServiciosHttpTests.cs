using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Seed;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;
namespace Millet.Api.IntegrationTests.ComercioExterior;

[Collection(ApiCompartida.Nombre)]
public sealed class CierreServiciosHttpTests
{
    [Fact]
    public async Task Consulta_Tesoreria_suma_solo_pagos_vigentes_de_la_empresa()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var scope = e.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var tes = scope.ServiceProvider.GetRequiredService<Millet.Tesoreria.Infrastructure.Persistence.TesoreriaDbContext>();
        var cuenta = await tes.CuentasBancarias.FirstAsync(c => c.EmpresaId == DemoSesionSeedHostedService.EmpresaId && c.Activa && c.Moneda == "MXN");
        var factura = Guid.NewGuid(); var proveedor = Guid.NewGuid();
        foreach (var monto in new[] { 40, 60 })
        {
            var movimiento = Millet.Tesoreria.Domain.Movimientos.MovimientoBancario.RegistrarPagoProveedor(cuenta.EmpresaId, cuenta, proveedor,
                monto, new(2026, 11, 11), "PRUEBA-SALDO-CE", null, e.Responsable, e.Ahora, fondosVerificados: true);
            tes.MovimientosBancarios.Add(movimiento);
            var aplicacion = new Millet.Tesoreria.Domain.Movimientos.AplicacionPagoProveedor(movimiento.Id, factura, proveedor, monto, e.Ahora);
            if (monto == 60) aplicacion.Revertir("Reversa ficticia de prueba");
            tes.AplicacionesPagoProveedor.Add(aplicacion);
        }
        await tes.SaveChangesAsync();
        var port = scope.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Application.PublicPorts.IPagosFacturaCeReadPort>();
        Assert.Equal(40, (await port.ImportesVigentesAsync(cuenta.EmpresaId, [factura], default))[factura]);
        Assert.Empty(await port.ImportesVigentesAsync(Guid.NewGuid(), [factura], default));
        Assert.Empty(await port.ImportesVigentesAsync(cuenta.EmpresaId, [Guid.NewGuid()], default));
    }

    [Fact]
    public async Task Cierre_HTTP_relee_CxP_impago_parcial_y_aplicacion_total_sin_duplicar_gasto()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var client = await e.ClienteAsync(true);
        const string ruta = "/api/v1/comercio-exterior/expedientes";
        using var creado = await client.PostAsJsonAsync(ruta, e.Importacion()); creado.EnsureSuccessStatusCode();
        var json = await creado.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("expediente").GetProperty("id").GetGuid();
        Guid facturaId;
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var ce = scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>();
            var cxp = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var exp = await ce.Expedientes.SingleAsync(x => x.Id == id);
            var oc = await scope.ServiceProvider.GetRequiredService<Millet.Compras.Infrastructure.ComprasDbContext>().OrdenesCompra.SingleAsync(x => x.Id == exp.OcId);
            var concepto = new ConceptoGasto(exp.EmpresaId, "CIERRE-PRUEBA", "Servicio ficticio de prueba"); ce.ConceptosGasto.Add(concepto);
            var f = Factura.CapturarConOc(exp.EmpresaId, null, null, oc.ProveedorId, exp.SucursalId, "PRUEBA-CE03", null,
                e.Ahora, e.Ahora, new(2026, 12, 1), "MXN", null, 100, 0, 0, 0, 100, oc.Id, null, Tolerancia.MontoAbsoluto(0.99m), 0, 0, e.Ahora);
            f.Autorizar(null, e.Ahora); cxp.FacturasProveedor.Add(f); await cxp.SaveChangesAsync(); facturaId = f.Id;
            exp.GuardarGasto(Guid.NewGuid(), new(concepto.Id, oc.ProveedorId, "PRUEBA-CE03", "MXN", 100, new(2026, 11, 11), FacturaProveedorId: f.Id), concepto, 1, "MXN");
            exp.ConfirmarDistribucion(exp.PrepararDistribucion([concepto],
                [new BasePartidaCosto(Guid.NewGuid(), null, "Base ficticia para aislar el cierre financiero", 100, 1, 1, OcId: oc.Id)],
                e.Responsable, e.Ahora, incrementablesAlInventario: false));
            await ce.SaveChangesAsync();
        }
        foreach (var aplicacion in new[] { 0, 40, 60 })
        {
            if (aplicacion > 0)
            {
                using var scope = e.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var cxp = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
                (await cxp.FacturasProveedor.SingleAsync(f => f.Id == facturaId)).AplicarNotaCredito(aplicacion, new(2026, 11, 11));
                await cxp.SaveChangesAsync();
            }
            using var actual = await client.GetAsync($"{ruta}/{id}"); actual.EnsureSuccessStatusCode();
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{ruta}/{id}/cerrar-financiero") { Content = JsonContent.Create(new { }) };
            req.Headers.Add("If-Match", actual.Headers.ETag!.Tag); req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var cierre = await client.SendAsync(req);
            if (aplicacion == 60) Assert.True(cierre.IsSuccessStatusCode, await cierre.Content.ReadAsStringAsync());
            else
            {
                Assert.Equal(HttpStatusCode.UnprocessableEntity, cierre.StatusCode);
                Assert.Contains("CE_SERVICIOS_PENDIENTES", await cierre.Content.ReadAsStringAsync());
                Assert.Contains("PRUEBA-CE03", await actual.Content.ReadAsStringAsync());
            }
        }
        using var final = e.Scope(); using var ignorar = final.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        Assert.Single((await final.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>().Expedientes.Include(x => x.Gastos).SingleAsync(x => x.Id == id)).Gastos);
    }
}
