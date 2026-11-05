using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// ALM-03: salida por rotura en dos pasos sobre /api/v1/almacen/salidas-rotura. El trigger de saldo corre en Postgres real:
/// criterios 1 (20 hojas a $900, rotura de 2: saldo 18 y $1,800), 2 (mismo usuario), 3 (25 con saldo 18) y 6 (sin permiso).
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class SalidasRoturaTests(WebApplicationFactory<Program> factory)
{
    private const string Ruta = "/api/v1/almacen/salidas-rotura";

    private async Task<(Guid ArticuloId, Guid BinId)> SembrarAsync()
    {
        using var admin = await ContabTestKit.LoginAsync(factory);
        await ContabTestKit.AsegurarPeriodosAbiertosAsync(admin, FechaContable.HoyLocal(DateTimeOffset.UtcNow));
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var n = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var art = new Articulo(Guid.CreateVersion7(), $"ROT-{n[..12]}", "Vidrio claro 6 mm hoja", "PZA");
        compartido.Add(art);
        await compartido.SaveChangesAsync();
        var mid = await compartido.Sucursales.AsNoTracking().Where(s => s.Clave == "MID").Select(s => s.Id).SingleAsync();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"R-{n[..16]}", "Roturas", mid);
        var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "R", "R", TipoSubAlmacen.Insumos);
        var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "R-01", "Rack 01");
        db.AddRange(alm, sub, bin, new SaldoInventario(bin.Id, sub.Id, art.Id, 20m, 900m, null));
        await db.SaveChangesAsync();
        return (art.Id, bin.Id);
    }

    private async Task<decimal> SaldoAsync(Guid articuloId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<AlmacenDbContext>().SaldosInventario.AsNoTracking()
            .Where(s => s.ArticuloId == articuloId).SumAsync(s => s.CantidadDisponible);
    }

    private static async Task<Guid> CapturarAsync(HttpClient c, Guid articuloId, Guid binId, decimal cantidad)
    {
        using var r = await c.PostAsJsonAsync(Ruta, new { ubicacionId = binId, articuloId, cantidad, motivoCodigo = "rotura_rack" });
        Assert.True(r.StatusCode == HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await ContabTestKit.Json(r)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Rotura_de_2_de_20_hojas_a_900_se_captura_sin_mover_saldo_y_al_autorizar_deja_18_y_1800()
    {
        var (art, bin) = await SembrarAsync();
        var almacenista = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenSalidasRoturaCapturar,
            PermisosCanonicos.AlmacenSalidasLeerTodas, PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales, PermisosCanonicos.AlmacenSalidasLeerTodasSucursales);
        var jefe = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenSalidasRoturaAutorizar,
            PermisosCanonicos.AlmacenSalidasLeerTodas, PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales, PermisosCanonicos.AlmacenSalidasLeerTodasSucursales);
        try
        {
            var id = await CapturarAsync(almacenista, art, bin, 2m);
            Assert.Equal(20m, await SaldoAsync(art));

            using (var sinFacultad = await almacenista.PostAsync($"{Ruta}/{id}/autorizar", null))
                Assert.Equal(HttpStatusCode.Forbidden, sinFacultad.StatusCode);

            using var ok = await jefe.PostAsync($"{Ruta}/{id}/autorizar", null);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var j = await ContabTestKit.Json(ok);
            Assert.Equal("AUTORIZADA", j.GetProperty("estado").GetString());
            Assert.Equal(1800m, j.GetProperty("valorMxn").GetDecimal());
            Assert.StartsWith("M-ROT", j.GetProperty("folioMovimiento").GetString());
            Assert.Equal(18m, await SaldoAsync(art));

            using var otra = await jefe.PostAsync($"{Ruta}/{id}/autorizar", null);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, otra.StatusCode);
            Assert.Equal(18m, await SaldoAsync(art));
        }
        finally
        {
            await ContabTestKit.LimpiarClienteAsync(almacenista);
            await ContabTestKit.LimpiarClienteAsync(jefe);
        }
    }

    [Fact]
    public async Task El_mismo_usuario_no_autoriza_su_captura_y_sin_permiso_la_API_responde_prohibido()
    {
        var (art, bin) = await SembrarAsync();
        var ambos = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenSalidasRoturaCapturar,
            PermisosCanonicos.AlmacenSalidasRoturaAutorizar, PermisosCanonicos.AlmacenSalidasLeerTodas,
            PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales, PermisosCanonicos.AlmacenSalidasLeerTodasSucursales);
        var lector = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenSalidasLeerTodas);
        try
        {
            var id = await CapturarAsync(ambos, art, bin, 2m);
            using var propia = await ambos.PostAsync($"{Ruta}/{id}/autorizar", null);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, propia.StatusCode);
            Assert.Equal("ROTURA_MISMO_USUARIO", (await ContabTestKit.Json(propia)).GetProperty("code").GetString());
            Assert.Equal(20m, await SaldoAsync(art));

            using var captura = await lector.PostAsJsonAsync(Ruta, new { ubicacionId = bin, articuloId = art, cantidad = 1m, motivoCodigo = "rotura_rack" });
            Assert.Equal(HttpStatusCode.Forbidden, captura.StatusCode);
            using var decide = await lector.PostAsJsonAsync($"{Ruta}/{id}/rechazar", new { motivo = "x" });
            Assert.Equal(HttpStatusCode.Forbidden, decide.StatusCode);
            using var lista = await lector.GetAsync(Ruta);
            Assert.Equal(HttpStatusCode.Forbidden, lista.StatusCode);
        }
        finally
        {
            await ContabTestKit.LimpiarClienteAsync(ambos);
            await ContabTestKit.LimpiarClienteAsync(lector);
        }
    }

    [Fact]
    public async Task Rotura_de_25_con_saldo_18_se_rechaza_al_autorizar_por_saldo_insuficiente()
    {
        var (art, bin) = await SembrarAsync();
        var almacenista = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenSalidasRoturaCapturar,
            PermisosCanonicos.AlmacenSalidasLeerTodas, PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales, PermisosCanonicos.AlmacenSalidasLeerTodasSucursales);
        var jefe = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenSalidasRoturaAutorizar,
            PermisosCanonicos.AlmacenSalidasLeerTodas, PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales, PermisosCanonicos.AlmacenSalidasLeerTodasSucursales);
        try
        {
            var primera = await CapturarAsync(almacenista, art, bin, 2m);
            using (var ok = await jefe.PostAsync($"{Ruta}/{primera}/autorizar", null)) Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var id = await CapturarAsync(almacenista, art, bin, 25m);

            using var r = await jefe.PostAsync($"{Ruta}/{id}/autorizar", null);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Equal("ROTURA_SALDO_INSUFICIENTE", (await ContabTestKit.Json(r)).GetProperty("code").GetString());
            Assert.Equal(18m, await SaldoAsync(art));

            using var rechazo = await jefe.PostAsJsonAsync($"{Ruta}/{id}/rechazar", new { motivo = "No hay 25 hojas en el rack" });
            Assert.Equal(HttpStatusCode.OK, rechazo.StatusCode);
            Assert.Equal("RECHAZADA", (await ContabTestKit.Json(rechazo)).GetProperty("estado").GetString());
        }
        finally
        {
            await ContabTestKit.LimpiarClienteAsync(almacenista);
            await ContabTestKit.LimpiarClienteAsync(jefe);
        }
    }
}
