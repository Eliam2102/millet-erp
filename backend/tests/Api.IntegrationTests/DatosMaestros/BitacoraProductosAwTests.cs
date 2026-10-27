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
[Collection(ApiCompartida.Nombre)]
public sealed class BitacoraProductosAwTests(WebApplicationFactory<Program> factory)
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

    private static readonly Guid UnidadKg = Guid.Parse("00000002-0007-0000-0000-000000000003");

    /// <summary>Edición del operador por el endpoint real: PATCH /productos-aw/{id}.</summary>
    private static async Task EditarEnErp(WebApplicationFactory<Program> f, string referencia, string? descripcion = null, Guid? unidadMedidaId = null)
    {
        var id = (await Producto(f, referencia)).Id;
        var http = await UsuariosAwHttp.SuperAdminAsync(f);
        var resp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"{Base}/{id}")
        {
            Content = JsonContent.Create(new { descripcion, unidadMedidaId }),
        });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    private static async Task ResolverAsync(WebApplicationFactory<Program> f, CambioMaestroAw c, string resolucion)
    {
        var http = await UsuariosAwHttp.SuperAdminAsync(f);
        Assert.Equal(HttpStatusCode.NoContent,
            (await http.PostAsJsonAsync($"{Base}/conflictos-aw/{c.Id}/resolver", new { resolucion, version = c.Version })).StatusCode);
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
        var aplicado = Assert.Single(await Filas(factory, r), c => c.Tipo == TipoCambioMaestroAw.Aplicado && c.Campo == "Descripción"
            && c.ValorAntes == "EDITADA ERP");
        Assert.Equal("DESC AW 2", aplicado.ValorDespues); Assert.Equal(EstadoCambioMaestroAw.Informativo, aplicado.Estado);
    }

    [Fact]
    public async Task Mantener_erp_resuelve_el_conflicto_de_descripcion_y_otro_cambio_de_a_w_no_lo_re_crea()
    {
        var r = NuevaRef();
        await Aplicar(factory, Snap(r, "DESC AW 1"));
        await EditarEnErp(factory, r, "EDITADA ERP");
        var nueva = Snap(r, "DESC AW 2") with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(1) };
        await Aplicar(factory, nueva);
        var conflicto = Assert.Single(await Filas(factory, r), c => c.Tipo == TipoCambioMaestroAw.EditadoEnErp);

        await ResolverAsync(factory, conflicto, "MantenerErp");
        var resuelto = Assert.Single(await Filas(factory, r), c => c.Tipo == TipoCambioMaestroAw.EditadoEnErp);
        Assert.Equal(EstadoCambioMaestroAw.Resuelto, resuelto.Estado); Assert.Equal("MantenerErp", resuelto.Resolucion);
        Assert.Equal("EDITADA ERP", (await Producto(factory, r)).Descripcion);
        Assert.DoesNotContain(await Filas(factory, r), c => c.Tipo == TipoCambioMaestroAw.Aplicado && c.Campo == "Descripción" && c.ValorDespues == "DESC AW 2");

        // A+W cambia OTRO campo (medidas → hash nuevo) y la descripción de A+W sigue igual: no se re-crea el conflicto.
        await Aplicar(factory, Snap(r, "DESC AW 2", medidas: ["1000x2000"]) with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(2) });
        Assert.Single(await Filas(factory, r), c => c.Tipo == TipoCambioMaestroAw.EditadoEnErp);
        Assert.Equal("EDITADA ERP", (await Producto(factory, r)).Descripcion);

        // Si A+W cambia ESE campo, sí vuelve a señalarse.
        await Aplicar(factory, Snap(r, "DESC AW 3", medidas: ["1000x2000"]) with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(3) });
        Assert.Equal(1, (await Filas(factory, r)).Count(c => c.Tipo == TipoCambioMaestroAw.EditadoEnErp && c.Estado == EstadoCambioMaestroAw.Pendiente));
        Assert.Equal("EDITADA ERP", (await Producto(factory, r)).Descripcion);
    }

    [Fact]
    public async Task Unidad_editada_en_el_erp_no_se_pisa_deja_conflicto_y_se_resuelve_con_mantener_erp_o_aplicar_aw()
    {
        var r = NuevaRef();
        var alta = Snap(r, "VIDRIO U");
        await Aplicar(factory, alta);
        await EditarEnErp(factory, r, unidadMedidaId: UnidadKg);
        Assert.Equal("KG", (await Producto(factory, r)).UnidadMedida);

        var nueva = alta with { Descripcion = "VIDRIO U 2", LeidoEnUtc = DateTime.UtcNow.AddSeconds(1) };
        var res = await Aplicar(factory, nueva);
        Assert.Equal(AplicarProductoAwAccion.Actualizado, res.Accion);
        var p = await Producto(factory, r);
        Assert.Equal("KG", p.UnidadMedida); Assert.Equal("VIDRIO U 2", p.Descripcion); // la unidad del ERP se conserva
        var conflicto = Assert.Single(await Filas(factory, r), c => c.Tipo == TipoCambioMaestroAw.EditadoEnErp);
        Assert.Equal("Unidad", conflicto.Campo); Assert.Equal(EstadoCambioMaestroAw.Pendiente, conflicto.Estado);
        Assert.Equal("KG", conflicto.ValorAntes); Assert.Equal("PZA", conflicto.ValorDespues);
        Assert.DoesNotContain(await Filas(factory, r), c => c.Campo == "Unidad" && c.Tipo == TipoCambioMaestroAw.Aplicado);

        var total = (await Filas(factory, r)).Count;
        await Aplicar(factory, nueva with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(2) });
        Assert.Equal(total, (await Filas(factory, r)).Count);

        await ResolverAsync(factory, conflicto, "MantenerErp");
        await Aplicar(factory, nueva with { Descripcion = "VIDRIO U 3", LeidoEnUtc = DateTime.UtcNow.AddSeconds(3) });
        Assert.Equal("KG", (await Producto(factory, r)).UnidadMedida);
        Assert.Single(await Filas(factory, r), c => c.Campo == "Unidad" && c.Tipo == TipoCambioMaestroAw.EditadoEnErp);

        // A+W cambia la unidad (L): nuevo conflicto; «Aplicar A+W» la restituye y deja fila Aplicado.
        await Aplicar(factory, (nueva with { Descripcion = "VIDRIO U 3", UnidadCruda = "L", LeidoEnUtc = DateTime.UtcNow.AddSeconds(4) }));
        var nuevoConflicto = Assert.Single(await Filas(factory, r), c => c.Campo == "Unidad" && c.Tipo == TipoCambioMaestroAw.EditadoEnErp
            && c.Estado == EstadoCambioMaestroAw.Pendiente);
        Assert.Equal("L", nuevoConflicto.ValorDespues);
        await ResolverAsync(factory, nuevoConflicto, "AplicarAw");
        Assert.Equal("L", (await Producto(factory, r)).UnidadMedida);
        var aplicado = Assert.Single(await Filas(factory, r), c => c.Campo == "Unidad" && c.Tipo == TipoCambioMaestroAw.Aplicado);
        Assert.Equal("KG", aplicado.ValorAntes); Assert.Equal("L", aplicado.ValorDespues);
    }

    [Fact]
    public async Task Baja_reactivacion_baja_deja_dos_bajas_y_un_reenvio_identico_no_agrega_filas()
    {
        var r = NuevaRef();
        await Aplicar(factory, Snap(r, "VIDRIO FLIP"));
        await Aplicar(factory, Snap(r, "VIDRIO FLIP", baja: true) with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(1) });
        await Aplicar(factory, Snap(r, "VIDRIO FLIP") with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(2) });
        var segunda = Snap(r, "VIDRIO FLIP", baja: true) with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(3) };
        await Aplicar(factory, segunda);

        var antes = await Filas(factory, r);
        Assert.Equal(2, antes.Count(c => c.Tipo == TipoCambioMaestroAw.Baja));
        Assert.Equal(1, antes.Count(c => c.Tipo == TipoCambioMaestroAw.Reactivacion));
        await Aplicar(factory, segunda with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(4) });
        Assert.Equal(antes.Count, (await Filas(factory, r)).Count);
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
