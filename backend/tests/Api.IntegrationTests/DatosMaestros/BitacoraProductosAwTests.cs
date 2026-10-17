using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.DatosMaestros.Domain;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>ADM-06 R3: bitácora de productos A+W, descripción editada en el ERP como conflicto, baja/reactivación y variantes.</summary>
public sealed class BitacoraProductosAwTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Base = "/api/v1/datos-maestros/productos-aw";
    private static string NuevaRef() => "BP" + Guid.NewGuid().ToString("N")[..10];

    private static AplicarProductoAwSnapshot Snap(string referencia, string descripcion, bool baja = false,
        string? modelo = null, params string[] medidas) => new(
        referencia, descripcion, "PZA", baja,
        medidas.Select(m => { var p = m.Split('x'); return new ProductoAwVarianteDato(m, decimal.Parse(p[0]), decimal.Parse(p[1]), 6m, null); }).ToList(),
        DateTime.UtcNow, "1", "m1", CodigoModelo: modelo);

    private static async Task<AplicarProductoAwResultado> Aplicar(WebApplicationFactory<Program> f, AplicarProductoAwSnapshot s)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AplicarProductoAwService>().AplicarAsync(s, CancellationToken.None);
    }

    private static async Task<List<CambioMaestroAw>> Filas(WebApplicationFactory<Program> f, string referencia)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().CambiosMaestrosAw.AsNoTracking()
            .Where(c => c.TipoEntidad == "ProductoAw" && c.ReferenciaExterna == referencia).ToListAsync();
    }

    private static async Task<ProductoAw> Producto(WebApplicationFactory<Program> f, string referencia)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().ProductosAw.AsNoTracking()
            .Include(p => p.Variantes).SingleAsync(p => p.ReferenciaExterna == referencia);
    }

    private static async Task EditarEnErp(WebApplicationFactory<Program> f, string referencia, string descripcion)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var p = await db.ProductosAw.SingleAsync(x => x.ReferenciaExterna == referencia);
        p.ActualizarDatos(descripcion: descripcion);
        await db.SaveChangesAsync();
    }

    private static AplicarProductoAwSnapshot Despues(AplicarProductoAwSnapshot s, int segundos) =>
        s with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(segundos) };

    [Fact]
    public async Task Variantes_por_medida_se_conservan_y_otro_producto_con_el_mismo_modelo_no_se_mezcla()
    {
        var r1 = NuevaRef(); var r2 = NuevaRef(); var modelo = "MOD" + Guid.NewGuid().ToString("N")[..6];
        var s = Snap(r1, "VIDRIO A", modelo: modelo, medidas: ["2440x3660", "2600x3600"]);
        var a = await Aplicar(factory, s);
        Assert.Equal(2, (await Producto(factory, r1)).Variantes.Count);
        await Aplicar(factory, Despues(s, 1));
        Assert.Equal(2, (await Producto(factory, r1)).Variantes.Count);

        var b = await Aplicar(factory, Snap(r2, "VIDRIO B", modelo: modelo, medidas: ["2440x3660"]));
        Assert.NotEqual(a.Producto!.Id, b.Producto!.Id);
        Assert.Equal(2, (await Producto(factory, r1)).Variantes.Count);
        Assert.Single((await Producto(factory, r2)).Variantes);
    }

    [Fact]
    public async Task Descripcion_editada_en_el_erp_no_se_pisa_deja_un_conflicto_y_aplicar_aw_la_restituye()
    {
        var r = NuevaRef();
        await Aplicar(factory, Snap(r, "DESC AW 1"));
        await EditarEnErp(factory, r, "EDITADA ERP");

        var nueva = Snap(r, "DESC AW 2") with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(1) };
        var res = await Aplicar(factory, nueva);
        Assert.Equal(AplicarProductoAwAccion.Actualizado, res.Accion);
        Assert.Equal("EDITADA ERP", (await Producto(factory, r)).Descripcion);
        var conflicto = Assert.Single(await Filas(factory, r), c => c.Tipo == TipoCambioMaestroAw.EditadoEnErp);
        Assert.Equal(EstadoCambioMaestroAw.Pendiente, conflicto.Estado);
        Assert.Equal("EDITADA ERP", conflicto.ValorAntes); Assert.Equal("DESC AW 2", conflicto.ValorDespues);

        await Aplicar(factory, Despues(nueva, 2));
        Assert.Single(await Filas(factory, r), c => c.Tipo == TipoCambioMaestroAw.EditadoEnErp);
        Assert.Equal("EDITADA ERP", (await Producto(factory, r)).Descripcion);

        var http = await UsuariosAwHttp.SuperAdminAsync(factory);
        var lista = await http.GetFromJsonAsync<JsonElement>($"{Base}/conflictos-aw?estado=Pendiente");
        Assert.Contains(lista.EnumerateArray(), e => e.GetProperty("id").GetGuid() == conflicto.Id);
        Assert.Equal(HttpStatusCode.NoContent,
            (await http.PostAsJsonAsync($"{Base}/conflictos-aw/{conflicto.Id}/resolver", new { resolucion = "AplicarAw", version = conflicto.Version })).StatusCode);
        Assert.Equal("DESC AW 2", (await Producto(factory, r)).Descripcion);
        var resuelto = (await Filas(factory, r)).Single(c => c.Tipo == TipoCambioMaestroAw.EditadoEnErp);
        Assert.Equal(EstadoCambioMaestroAw.Resuelto, resuelto.Estado); Assert.Equal("AplicarAw", resuelto.Resolucion);
    }

    [Fact]
    public async Task Cambios_de_descripcion_unidad_y_medidas_quedan_en_la_bitacora_y_se_leen_por_endpoint()
    {
        var r = NuevaRef();
        var primera = await Aplicar(factory, Snap(r, "ORIGINAL", medidas: ["1000x2000"]));
        await Aplicar(factory, Snap(r, "CAMBIADA", medidas: ["1000x2000", "1100x2100"]) with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(1) });
        var campos = (await Filas(factory, r)).Select(c => c.Campo).ToList();
        Assert.Contains("Descripción", campos); Assert.Contains("Medidas (variantes)", campos);

        var http = await UsuariosAwHttp.SuperAdminAsync(factory);
        var cambios = await http.GetFromJsonAsync<JsonElement>($"{Base}/{primera.Producto!.Id}/cambios-aw");
        Assert.True(cambios.GetArrayLength() >= 2);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"{Base}/{Guid.NewGuid()}/cambios-aw")).StatusCode);
    }

    [Fact]
    public async Task Baja_y_reactivacion_desde_aw_conservan_el_mismo_producto_y_dejan_filas()
    {
        var r = NuevaRef();
        var alta = await Aplicar(factory, Snap(r, "VIDRIO"));
        var baja = Snap(r, "VIDRIO", baja: true) with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(1) };
        await Aplicar(factory, baja);
        Assert.Equal(EstatusCatalogo.Inactivo, (await Producto(factory, r)).Estatus);
        await Aplicar(factory, Snap(r, "VIDRIO") with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(2) });

        var p = await Producto(factory, r);
        Assert.Equal(alta.Producto!.Id, p.Id); Assert.Equal(EstatusCatalogo.Activo, p.Estatus);
        var tipos = (await Filas(factory, r)).Select(c => c.Tipo).ToList();
        Assert.Contains(TipoCambioMaestroAw.Baja, tipos); Assert.Contains(TipoCambioMaestroAw.Reactivacion, tipos);
    }

    [Fact]
    public async Task Reintento_por_referencia_sobre_un_producto_editado_en_el_erp_sigue_dando_200()
    {
        var r = NuevaRef();
        var ruta = Path.Combine(Path.GetTempPath(), $"prod-aw-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(ruta, $$$"""
            {"origen":{"vw_erp_articulo":[{"producto_ref":"{{{r}}}","descripcion":"DESC AW","unidad_medida":"PZA",
             "baja":false,"variantes":[],"TRANSACTION_TIME":"2026-09-01T10:00:00"}]}}
            """);
        await using var f = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("IntegracionesAw:Productos:OrigenHabilitado", "true");
            b.UseSetting("IntegracionesAw:Productos:ArchivoSimulado", ruta);
        });
        var http = await UsuariosAwHttp.SuperAdminAsync(f);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync($"{Base}/sincronizacion/{r}", null)).StatusCode);
        await EditarEnErp(f, r, "EDITADA ERP");

        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync($"{Base}/sincronizacion/{r}", null)).StatusCode);
        Assert.Equal("EDITADA ERP", (await Producto(f, r)).Descripcion);
        Assert.Contains(await Filas(f, r), c => c.Tipo == TipoCambioMaestroAw.EditadoEnErp && c.Estado == EstadoCambioMaestroAw.Pendiente);
    }
}
