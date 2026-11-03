using System.Net;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Cargas;

namespace Millet.Api.IntegrationTests.DatosMaestros;

[Collection(ApiCompartida.Nombre)]
public sealed class CargasMaestrosEndpointsTests(WebApplicationFactory<Program> factory)
{
    [Theory]
    [InlineData("articulos")]
    [InlineData("proveedores")]
    [InlineData("clientes")]
    public async Task Previa_no_escribe_confirmar_omite_errores_y_repetir_es_sin_cambio(string catalogo)
    {
        using var client = await LoginAsync();
        var clave = "CM" + Guid.NewGuid().ToString("N")[..10];
        var csv = await CsvAsync(catalogo, clave);
        var previa = await CargarAsync(client, catalogo, csv, false);
        Assert.Equal(1, previa.Nuevos); Assert.Equal(1, previa.Errores); Assert.False(previa.Confirmado);
        Assert.False(await ExisteAsync(catalogo, clave));
        var resultado = await CargarAsync(client, catalogo, csv, true, previa.Huella);
        Assert.True(resultado.Confirmado); Assert.Equal(1, resultado.Nuevos); Assert.Equal(1, resultado.Errores);
        Assert.True(await ExisteAsync(catalogo, clave));
        var repetida = await CargarAsync(client, catalogo, csv, true, previa.Huella);
        Assert.Equal(0, repetida.Nuevos); Assert.Equal(1, repetida.SinCambio); Assert.Equal(1, repetida.Errores);
    }
    [Theory]
    [InlineData("articulos")]
    [InlineData("proveedores")]
    public async Task Reactivar_es_idempotente_y_persiste_activo(string catalogo)
    {
        using var client = await LoginAsync(); var clave = "RA" + Guid.NewGuid().ToString("N")[..10];
        var csv = await CsvAsync(catalogo, clave); var previa = await CargarAsync(client, catalogo, csv, false);
        await CargarAsync(client, catalogo, csv, true, previa.Huella);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var id = catalogo == "articulos" ? await db.Articulos.Where(x => x.Clave == clave).Select(x => x.Id).SingleAsync()
            : await db.Proveedores.Where(x => x.ClaveLegacy == clave).Select(x => x.Id).SingleAsync();
        (await client.DeleteAsync($"/api/v1/catalogos/{catalogo}/{id}")).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/v1/catalogos/{catalogo}/{id}/reactivar", null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/v1/catalogos/{catalogo}/{id}/reactivar", null)).EnsureSuccessStatusCode();
        var estado = catalogo == "articulos" ? await db.Articulos.AsNoTracking().Where(x => x.Id == id).Select(x => x.Estatus).SingleAsync()
            : await db.Proveedores.AsNoTracking().Where(x => x.Id == id).Select(x => x.Estatus).SingleAsync();
        Assert.Equal(EstatusCatalogo.Activo, estado);
    }
    [Fact]
    public async Task Huella_distinta_no_aplica_y_llave_repetida_devuelve_respuesta_original()
    {
        using var client = await LoginAsync(); var clave = "HU" + Guid.NewGuid().ToString("N")[..10];
        var csv = await CsvAsync("proveedores", clave); var previa = await CargarAsync(client, "proveedores", csv, false);
        using var invalida = await EnviarAsync(client, "proveedores", csv, true, "HUELLA-DISTINTA");
        Assert.Equal(HttpStatusCode.Conflict, invalida.StatusCode); Assert.False(await ExisteAsync("proveedores", clave));
        var llave = Guid.NewGuid().ToString();
        using var primera = await EnviarAsync(client, "proveedores", csv, true, previa.Huella, llave);
        var cuerpo = await primera.Content.ReadAsStringAsync(); primera.EnsureSuccessStatusCode();
        using var repetida = await EnviarAsync(client, "proveedores", csv, true, previa.Huella, llave);
        repetida.EnsureSuccessStatusCode(); Assert.Equal(cuerpo, await repetida.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData("clientes")][InlineData("articulos")][InlineData("proveedores")][InlineData("productos-aw-fiscal")]
    [InlineData("centros-costo-dim1")][InlineData("centros-costo-dim2")][InlineData("centros-costo-dim3")]
    public async Task Carga_sin_permiso_es_403(string catalogo)
    {
        using var client = await LoginAsync("sin-carga-" + Guid.NewGuid().ToString("N"));
        using var respuesta = await EnviarAsync(client, catalogo, "Clave,Nombre\nFICTICIO,Prueba", false);
        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        using var confirmar = await EnviarAsync(client, catalogo, "Clave,Nombre\nFICTICIO,Prueba", true, "huella");
        Assert.Equal(HttpStatusCode.Forbidden, confirmar.StatusCode);
    }
    [Fact]
    public async Task Duplicados_en_archivo_se_reportan_en_ambos_renglones()
    {
        using var client = await LoginAsync(); var clave = "DU" + Guid.NewGuid().ToString("N")[..10];
        var csv = $"card_code,group_name,prefijo,razon_social,nombre_comercial,rfc,email,telefono,currency,condiciones_pago_dias\n{clave},PROV. EXTRANJEROS,EXT,Proveedor ficticio,,XEXX010101000,,,##,0\n{clave},PROV. EXTRANJEROS,EXT,Otro ficticio,,XEXX010101000,,,##,0";
        var previa = await CargarAsync(client, "proveedores", csv, false);
        Assert.Equal(2, previa.Errores); Assert.All(previa.Renglones, r => Assert.Contains(r.Errores, e => e.Codigo == "DUPLICADO_EN_ARCHIVO"));
        var confirmar = await CargarAsync(client, "proveedores", csv, true, previa.Huella);
        Assert.Equal(2, confirmar.Errores); Assert.False(await ExisteAsync("proveedores", clave));
    }
    [Fact]
    public async Task Archivo_de_diez_megabytes_admite_el_sobre_multipart_en_confirmar()
    {
        using var client = await LoginAsync(); var clave = "MB" + Guid.NewGuid().ToString("N")[..10];
        var csv = (await CsvAsync("clientes", clave)).PadRight(ArchivoMaestros.LimiteBytes - 100, ' ');
        Assert.True(Encoding.UTF8.GetByteCount(csv) <= ArchivoMaestros.LimiteBytes);
        var previa = await CargarAsync(client, "clientes", csv, false);
        var resultado = await CargarAsync(client, "clientes", csv, true, previa.Huella);
        Assert.True(resultado.Confirmado); Assert.Equal(1, resultado.Nuevos);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Proveedor_cargado_activo_avisa_expediente_y_completa_con_documentos_o_excepciones(bool exceptuar)
    {
        await using var ambiente = new AdjuntosProveedorAmbiente(factory);
        using var client = await ambiente.SuperAdminAsync();
        var clave = "EX" + Guid.NewGuid().ToString("N")[..10];
        var csv = await CsvAsync("proveedores", clave);
        var previa = await CargarAsync(client, "proveedores", csv, false);
        await CargarAsync(client, "proveedores", csv, true, previa.Huella);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var proveedor = await db.Proveedores.AsNoTracking().SingleAsync(p => p.ClaveLegacy == clave);
        Assert.Equal(EstatusCatalogo.Activo, proveedor.Estatus);
        Assert.Equal("CargaSap", proveedor.OrigenCarga);
        var baseUrl = $"/api/v1/datos-maestros/proveedores/{proveedor.Id}";
        var listaUrl = $"/api/v1/datos-maestros/proveedores?q={proveedor.Clave}&soloExpedienteIncompleto=true";
        var lista = await client.GetFromJsonAsync<JsonElement>(listaUrl);
        var item = Assert.Single(lista.GetProperty("items").EnumerateArray());
        Assert.False(item.GetProperty("expedienteCompleto").GetBoolean());
        Assert.True(item.GetProperty("documentosFaltantes").GetInt32() > 0);
        var expediente = await client.GetFromJsonAsync<JsonElement>($"{baseUrl}/expediente");
        Assert.Equal(expediente.GetProperty("faltantes").GetArrayLength() + expediente.GetProperty("vencidos").GetArrayLength(), item.GetProperty("documentosFaltantes").GetInt32());
        foreach (var doc in expediente.GetProperty("documentos").EnumerateArray().Where(d => d.GetProperty("obligatorio").GetBoolean()))
        {
            if (exceptuar)
            {
                using var respuesta = await client.PostAsJsonAsync($"{baseUrl}/expediente/excepciones", new
                {
                    tipoDocumentoId = doc.GetProperty("tipoDocumentoId").GetGuid(), motivo = "Excepción sintética de integración",
                });
                respuesta.EnsureSuccessStatusCode();
            }
            else
            {
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(doc.GetProperty("tipoDocumentoId").GetString()!), "tipoDocumentoId");
                using var archivo = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4\nDocumento sintético"));
                archivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                form.Add(archivo, "archivo", "prueba.pdf");
                form.Add(new StringContent(DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd")), "vigenteHasta");
                using var respuesta = await client.PostAsync($"{baseUrl}/adjuntos", form);
                respuesta.EnsureSuccessStatusCode();
            }
        }
        lista = await client.GetFromJsonAsync<JsonElement>(listaUrl);
        Assert.Empty(lista.GetProperty("items").EnumerateArray());
        Assert.Equal(0, lista.GetProperty("total").GetInt32());
        lista = await client.GetFromJsonAsync<JsonElement>($"/api/v1/datos-maestros/proveedores?q={proveedor.Clave}");
        item = Assert.Single(lista.GetProperty("items").EnumerateArray());
        Assert.True(item.GetProperty("expedienteCompleto").GetBoolean());
        Assert.Equal(0, item.GetProperty("documentosFaltantes").GetInt32());
        (await client.PatchAsJsonAsync($"/api/v1/catalogos/proveedores/{proveedor.Id}", new { nombreComercial = "Edición sintética" })).EnsureSuccessStatusCode();
        var detalle = await client.GetFromJsonAsync<JsonElement>(baseUrl);
        Assert.Equal("CargaSap", detalle.GetProperty("origenCarga").GetString());
        Assert.Equal((int)EstatusCatalogo.Activo, detalle.GetProperty("estatus").GetInt32());
    }

    private async Task<HttpClient> LoginAsync(string oid = "dev-superadmin")
    {
        var client = factory.CreateClientWithIdempotency();
        using var login = await client.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = oid, Email = oid == "dev-superadmin" ? "superadmin@dev.local" : oid + "@example.test", Nombre = oid, EmpresaId = (Guid?)null });
        login.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
        return client;
    }
    private async Task<string> CsvAsync(string catalogo, string clave)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        if (catalogo == "articulos")
        {
            var unidad = await db.UnidadesMedida.Where(x => x.Estatus == EstatusCatalogo.Activo).Select(x => x.Codigo).FirstAsync();
            return $"item_code,nombre,unidad_medida,invnt_item,grupo_cod,categoria,ultima_compra\n{clave},Artículo ficticio,{unidad},N,100,,2026-01-01\n{clave}E,,{unidad},N,100,,";
        }
        if (catalogo == "proveedores") return $"card_code,group_name,prefijo,razon_social,nombre_comercial,rfc,email,telefono,currency,condiciones_pago_dias\n{clave},PROV. EXTRANJEROS,EXT,Proveedor ficticio,,XEXX010101000,,,##,30\n{clave}E,PROV. EXTRANJEROS,EXT,,,XEXX010101000,,,##,30";
        var rfc = "CMA261029" + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant();
        return $"Clave,Razón social,Clasificación,RFC,Régimen fiscal,Código postal fiscal,Uso CFDI,Forma de pago,Método de pago,Moneda\n{clave},Cliente ficticio,00000012-0001-0000-0000-000000000001,{rfc},601,97000,G03,03,PUE,MXN\n{clave}E,,00000012-0001-0000-0000-000000000001,,,,,,,";
    }
    private async Task<bool> ExisteAsync(string catalogo, string clave)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        return catalogo switch { "articulos" => await db.Articulos.AnyAsync(x => x.Clave == clave), "proveedores" => await db.Proveedores.AnyAsync(x => x.ClaveLegacy == clave), _ => await db.Clientes.AnyAsync(x => x.Clave == clave) };
    }
    internal static async Task<ResultadoMaestros> CargarAsync(HttpClient client, string catalogo, string csv, bool confirmar, string? huella = null)
    {
        using var respuesta = await EnviarAsync(client, catalogo, csv, confirmar, huella);
        respuesta.EnsureSuccessStatusCode(); return (await respuesta.Content.ReadFromJsonAsync<ResultadoMaestros>())!;
    }
    private static async Task<HttpResponseMessage> EnviarAsync(HttpClient client, string catalogo, string csv, bool confirmar, string? huella = null, string? llave = null)
    {
        using var form = new MultipartFormDataContent("millet-carga-prueba");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "archivo", "maestros.csv");
        if (huella is not null) form.Add(new StringContent(huella), "huella");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/datos-maestros/cargas/{catalogo}/{(confirmar ? "confirmar" : "previa")}") { Content = form };
        if (llave is not null) request.Headers.Add("Idempotency-Key", llave);
        return await client.SendAsync(request);
    }
}
