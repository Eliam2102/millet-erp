using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Millet.ComercioExterior.Domain;

namespace Millet.Api.IntegrationTests.ComercioExterior;

[Collection(ApiCompartida.Nombre)]
public sealed class P18FlujoHttpTests
{
    private const string Base = "/api/v1/comercio-exterior/expedientes";
    [Fact]
    public async Task Importacion_expone_contenedores_y_rechaza_embarque_maritimo_sin_naviera()
    {
        await using var escenario = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var c = await escenario.ClienteAsync(true);
        using var creado = await c.PostAsJsonAsync(Base, escenario.Importacion()); creado.EnsureSuccessStatusCode();
        var detalle = await Body(creado); var id = detalle.GetProperty("expediente").GetProperty("id").GetGuid();
        using var participante = await Send(c, $"{Base}/{id}/participantes", new { nombre = "Naviera pendiente de asignar", tipo = TipoParticipante.AgenteAduanal, papel = PapelParticipante.Captura }, creado.Headers.ETag!.Tag);
        participante.EnsureSuccessStatusCode();
        using var contenedor = await Send(c, $"{Base}/{id}/contenedores", new { numero = "P18-CONT", blGuia = "P18-BL", modalidad = ModalidadTransporte.Maritimo }, participante.Headers.ETag!.Tag);
        contenedor.EnsureSuccessStatusCode();
        var contenedores = await Body(contenedor); var cid = contenedores.GetProperty("contenedores")[0].GetProperty("id").GetGuid();
        Assert.Equal(0, contenedores.GetProperty("partidasImportacion").GetArrayLength());
        using var embarque = await Send(c, $"{Base}/{id}/contenedores/{cid}/embarcar", new { fecha = "2026-10-15" }, contenedor.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, embarque.StatusCode);
        Assert.Equal("CE_TRANSPORTISTA_REQUERIDO", (await Body(embarque)).GetProperty("code").GetString());
    }
    [Fact]
    public async Task Nueva_incidencia_sin_fecha_compromiso_no_se_registra()
    {
        await using var escenario = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var c = await escenario.ClienteAsync(true);
        using var creado = await c.PostAsJsonAsync(Base, escenario.Importacion()); creado.EnsureSuccessStatusCode();
        var id = (await Body(creado)).GetProperty("expediente").GetProperty("id").GetGuid();
        using var incidencia = await Send(c, $"{Base}/{id}/incidencias", new { tipo = TipoIncidencia.DocumentoIncorrecto, fecha = "2026-10-15", responsableUsuarioId = escenario.Responsable, motivo = "Documento incorrecto de prueba", siguienteAccion = "Solicitar corrección" }, creado.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, incidencia.StatusCode);
        using var actual = await c.GetAsync($"{Base}/{id}"); actual.EnsureSuccessStatusCode();
        Assert.Empty((await Body(actual)).GetProperty("incidencias").EnumerateArray());
    }
    private static Task<HttpResponseMessage> Send(HttpClient c, string ruta, object body, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return c.SendAsync(request);
    }
    private static async Task<JsonElement> Body(HttpResponseMessage r)
    { using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return d.RootElement.Clone(); }
}
