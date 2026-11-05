using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Mermas;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// A4.4: desperdicio de mesa y merma por etapa que informa A+W por POST /api/v1/integraciones/almacenes/mensajes, y la
/// autorización de la merma por /api/v1/almacen/mermas-proceso. Cada prueba siembra su artículo A+W y su saldo en MID.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class MermasProcesoTests(WebApplicationFactory<Program> factory)
{
    private const string Ruta = "/api/v1/integraciones/almacenes/mensajes";
    private const string Mermas = "/api/v1/almacen/mermas-proceso";
    private const string Llave = "llave-test-almacenes";

    private WebApplicationFactory<Program> ConLlave() =>
        factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Integraciones:AlmacenesApp:ApiKey"] = Llave })));

    /// <summary>Vidrio con 40 m² a 180 MXN/m² en MID.</summary>
    private async Task<(Guid ArticuloId, string ArticuloAw)> SembrarAsync()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var n = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var refAw = $"MER{n[..12]}";
        var prod = new ProductoAw(Guid.CreateVersion7(), refAw, "Vidrio claro 6 mm", "M2");
        var art = new Articulo(Guid.CreateVersion7(), $"MER-{n[12..24]}", "Vidrio claro 6 mm", "M2");
        art.AsignarProductoAw(prod.Id);
        compartido.AddRange(prod, art);
        await compartido.SaveChangesAsync();
        var mid = await compartido.Sucursales.AsNoTracking().Where(s => s.Clave == "MID").Select(s => s.Id).SingleAsync();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"M-{n[..16]}", "Mermas", mid);
        var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "M", "M", TipoSubAlmacen.Insumos);
        var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "M", "M");
        db.AddRange(alm, sub, bin, new SaldoInventario(bin.Id, sub.Id, art.Id, 40m, 180m, null));
        await db.SaveChangesAsync();
        return (art.Id, refAw);
    }

    private static HttpRequestMessage Req(string id, string tipo, object datos)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, Ruta)
        {
            Content = JsonContent.Create(new { mensajeId = id, tipo, version = 1, fecha = "2026-11-04T17:47:00-06:00", origen = "AlmacenRQ_MP", datos }),
        };
        r.Headers.Add("X-Mensaje-Id", id);
        r.Headers.Add("X-Millet-Almacenes-Key", Llave);
        return r;
    }

    private async Task<List<MermaProceso>> FilasAsync(Guid articuloId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<AlmacenDbContext>().MermasProceso.AsNoTracking()
            .Where(m => m.ArticuloId == articuloId).ToListAsync();
    }

    [Fact]
    public async Task A44_a_desperdicio_del_dia_por_mesa_queda_aplicado_y_el_reenvio_no_lo_duplica()
    {
        var s = await SembrarAsync();
        await using var f = ConLlave();
        var id = Guid.NewGuid().ToString();
        object datos = new
        {
            sucursal = "MID", fecha = "2026-11-04",
            mesas = new object[]
            {
                new { mesa = "HEGLA1", articuloAw = s.ArticuloAw, m2 = 3.2m },
                new { mesa = "HEGLA2", articuloAw = s.ArticuloAw, m2 = 2.5m },
            },
        };
        using var r1 = await f.CreateClient().SendAsync(Req(id, "DESPERDICIO_MESA", datos));
        var a1 = await ContabTestKit.Json(r1);
        Assert.Equal("RECIBIDO", a1.GetProperty("estado").GetString());

        var filas = await FilasAsync(s.ArticuloId);
        Assert.Equal(2, filas.Count);
        Assert.All(filas, x => Assert.Equal(MermaProceso.Aplicada, x.Estado));
        Assert.Equal(576m, filas.Single(x => x.Mesa == "HEGLA1").ValorMxn);

        using var r2 = await f.CreateClient().SendAsync(Req(id, "DESPERDICIO_MESA", datos));
        Assert.Equal("DUPLICADO", (await ContabTestKit.Json(r2)).GetProperty("estado").GetString());
        Assert.Equal(2, (await FilasAsync(s.ArticuloId)).Count);

        // Otro mensaje con la misma mesa y día: ERROR, sin tocar lo ya registrado.
        using var r3 = await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "DESPERDICIO_MESA", datos));
        Assert.Equal("ERROR", (await ContabTestKit.Json(r3)).GetProperty("estado").GetString());
        Assert.Equal(2, (await FilasAsync(s.ArticuloId)).Count);
    }

    [Fact]
    public async Task A44_b_c_merma_de_12_kg_en_templado_queda_pendiente_y_con_autorizacion_queda_aplicada_con_su_valor()
    {
        var s = await SembrarAsync();
        await using var f = ConLlave();
        using var r = await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "MERMA_PROCESO", new
        {
            sucursal = "MID", fecha = "2026-11-04", etapa = "templado", motivo = "Rotura en horno", articuloAw = s.ArticuloAw, kg = 12m, m2 = 0.8m,
        }));
        Assert.Equal("RECIBIDO", (await ContabTestKit.Json(r)).GetProperty("estado").GetString());
        var m = Assert.Single(await FilasAsync(s.ArticuloId));
        Assert.Equal(MermaProceso.Pendiente, m.Estado);   // A4.4-c: sin autorización no se aplica
        Assert.Equal(144m, m.ValorMxn);

        var autoriza = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenMermasAutorizar,
            PermisosCanonicos.AlmacenSalidasLeerTodas, PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales, PermisosCanonicos.AlmacenSalidasLeerTodasSucursales);
        var lector = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenSalidasLeerTodas, PermisosCanonicos.AlmacenSalidasLeerTodasSucursales);
        try
        {
            using (var sinFacultad = await lector.PostAsync($"{Mermas}/{m.Id}/autorizar", null))
                Assert.Equal(HttpStatusCode.Forbidden, sinFacultad.StatusCode);
            Assert.Equal(MermaProceso.Pendiente, (await FilasAsync(s.ArticuloId)).Single().Estado);

            using (var ok = await autoriza.PostAsync($"{Mermas}/{m.Id}/autorizar", null))
            {
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
                var j = await ContabTestKit.Json(ok);
                Assert.Equal("APLICADA", j.GetProperty("estado").GetString());
                Assert.Equal("templado", j.GetProperty("etapa").GetString());
            }
            Assert.Equal(MermaProceso.Aplicada, (await FilasAsync(s.ArticuloId)).Single().Estado);
            using (var otra = await autoriza.PostAsync($"{Mermas}/{m.Id}/autorizar", null))
                Assert.Equal(HttpStatusCode.UnprocessableEntity, otra.StatusCode);

            var lista = await ContabTestKit.Json(await lector.GetAsync($"{Mermas}?estado=APLICADA&tipo=MERMA"));
            Assert.Contains(lista.EnumerateArray(), x => x.GetProperty("id").GetGuid() == m.Id);
        }
        finally
        {
            await ContabTestKit.LimpiarClienteAsync(autoriza);
            await ContabTestKit.LimpiarClienteAsync(lector);
        }
    }

    [Fact]
    public async Task Merma_con_etapa_invalida_o_articulo_desconocido_da_ERROR_sin_registro()
    {
        var s = await SembrarAsync();
        await using var f = ConLlave();
        using var r1 = await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "MERMA_PROCESO", new
        {
            sucursal = "MID", fecha = "2026-11-04", etapa = "pulido", motivo = "x", articuloAw = s.ArticuloAw, kg = 1m, m2 = 0.1m,
        }));
        Assert.Equal("ERROR", (await ContabTestKit.Json(r1)).GetProperty("estado").GetString());
        using var r2 = await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "MERMA_PROCESO", new
        {
            sucursal = "MID", fecha = "2026-11-04", etapa = "corte", motivo = "x", articuloAw = "NO-EXISTE-MER", kg = 1m, m2 = 0.1m,
        }));
        Assert.Equal("ERROR", (await ContabTestKit.Json(r2)).GetProperty("estado").GetString());
        Assert.Empty(await FilasAsync(s.ArticuloId));
    }
}
