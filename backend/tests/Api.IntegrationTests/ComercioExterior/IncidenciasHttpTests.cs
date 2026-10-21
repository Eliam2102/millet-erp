using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Millet.ComercioExterior.Domain;

namespace Millet.Api.IntegrationTests.ComercioExterior;
public sealed class IncidenciasHttpTests
{
    private const string Base = "/api/v1/comercio-exterior/expedientes";
    [Fact]
    public async Task Incidencias_permiso_sucursal_version_idempotencia_y_cierre()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync(); using var c = await e.ClienteAsync(true);
        var creado = await c.PostAsJsonAsync(Base, e.Importacion()); creado.EnsureSuccessStatusCode();
        var id = (await Json(creado)).GetProperty("expediente").GetProperty("id").GetGuid(); var etag = creado.Headers.ETag!.Tag;
        var datos = new DatosIncidencia(TipoIncidencia.MercanciaDanada, new DateOnly(2026, 10, 15), e.Responsable, "FIX-CE mercancía dañada", HitoClave: "entregado");
        using var ninguno = await e.ClienteAsync(false); using var ajeno = await e.ClienteAsync(true, asociar: false);
        foreach (var client in new[] { ninguno, ajeno })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, $"{Base}/{id}/incidencias", datos, etag)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Base}/{id}/incidencias")).StatusCode);
        }
        var key = Guid.NewGuid().ToString(); var alta = await Send(c, $"{Base}/{id}/incidencias", datos, etag, key); alta.EnsureSuccessStatusCode();
        var repetida = await Send(c, $"{Base}/{id}/incidencias", datos, etag, key); repetida.EnsureSuccessStatusCode();
        Assert.Single((await Json(repetida)).GetProperty("incidencias").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(c, $"{Base}/{id}/incidencias", datos, etag)).StatusCode);
        var dto = await Json(alta); var incidencia = dto.GetProperty("incidencias")[0].GetProperty("id").GetGuid(); etag = alta.Headers.ETag!.Tag;
        Assert.True(dto.GetProperty("hitos").EnumerateArray().Single(h => h.GetProperty("clave").GetString() == "entregado").GetProperty("conIncidencia").GetBoolean());
        Assert.Single(dto.GetProperty("avisosIncidencias").EnumerateArray());
        foreach (var client in new[] { ninguno, ajeno })
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, $"{Base}/{id}/incidencias/{incidencia}/estado", new { estado = 2 }, etag)).StatusCode);
        var cerrado = await Send(c, $"{Base}/{id}/cerrar", null, etag);
        Assert.Equal("CE_INCIDENCIAS_PENDIENTES", (await Json(cerrado)).GetProperty("code").GetString());
        foreach (var body in new object[] { new { estado = 3 }, new { estado = 3, accionTomada = "Se repuso la pieza" } })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(c, $"{Base}/{id}/incidencias/{incidencia}/estado", body, etag)).StatusCode);
        var atencion = await Send(c, $"{Base}/{id}/incidencias/{incidencia}/estado", new { estado = 2, accionTomada = "Revisando descarga" }, etag); atencion.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Send(c, $"{Base}/{id}/incidencias/{incidencia}/estado", new { estado = 4, motivo = "Capturada por error" }, etag)).StatusCode);
        var cancelada = await Send(c, $"{Base}/{id}/incidencias/{incidencia}/estado", new { estado = 4, motivo = "Capturada por error" }, atencion.Headers.ETag!.Tag); cancelada.EnsureSuccessStatusCode();
        Assert.Equal(3, (await Json(cancelada)).GetProperty("incidencias")[0].GetProperty("historial").GetArrayLength());
        // Sin incidencias pendientes ni gastos o anticipos abiertos, el cierre financiero procede (CE-06).
        (await Send(c, $"{Base}/{id}/cerrar", null, cancelada.Headers.ETag!.Tag)).EnsureSuccessStatusCode();
        var reporte = await c.GetAsync($"/api/v1/comercio-exterior/incidencias?expedienteId={id}&soloAbiertas=true"); reporte.EnsureSuccessStatusCode();
        Assert.Empty((await Json(reporte)).GetProperty("filas").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await ajeno.GetAsync($"/api/v1/comercio-exterior/incidencias?expedienteId={id}")).StatusCode);
    }
    [Fact]
    public async Task Rectificacion_conserva_versiones_y_relacion_iva_usa_vigente()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync(); using var c = await e.ClienteAsync(true);
        var creado = await c.PostAsJsonAsync(Base, e.Importacion()); creado.EnsureSuccessStatusCode();
        var id = (await Json(creado)).GetProperty("expediente").GetProperty("id").GetGuid(); var etag = creado.Headers.ETag!.Tag;
        var tipo = await e.TipoAsync("IMP_PEDIMENTO"); var a = await e.AdjuntoAsync(id, tipo); var b = await e.AdjuntoAsync(id, tipo);
        var original = await Send(c, $"{Base}/{id}/documentos", new { tipoDocumentoId = tipo, adjuntoId = a, numeroPedimento = "FIX-ORIGINAL", noAplica = false }, etag); original.EnsureSuccessStatusCode();
        var alta = await Send(c, $"{Base}/{id}/incidencias", new DatosIncidencia(TipoIncidencia.RectificacionPedimento, new DateOnly(2026, 10, 15), e.Responsable, "FIX-CE rectificar pedimento", EvidenciasIds: [b]), original.Headers.ETag!.Tag); alta.EnsureSuccessStatusCode();
        var incidencia = (await Json(alta)).GetProperty("incidencias")[0].GetProperty("id").GetGuid();
        var atencion = await Send(c, $"{Base}/{id}/incidencias/{incidencia}/estado", new { estado = 2 }, alta.Headers.ETag!.Tag); atencion.EnsureSuccessStatusCode();
        var resuelta = await Send(c, $"{Base}/{id}/incidencias/{incidencia}/estado", new { estado = 3, accionTomada = "Se rectificó clasificación", nuevoPedimento = "FIX-RECTIFICADO", adjuntoPedimentoId = b }, atencion.Headers.ETag!.Tag); resuelta.EnsureSuccessStatusCode();
        var dto = await Json(resuelta); Assert.Equal(2, dto.GetProperty("documentos").GetArrayLength());
        Assert.Equal("FIX-ORIGINAL", dto.GetProperty("incidencias")[0].GetProperty("pedimentoAnterior").GetString());
        var reporte = await c.GetAsync("/api/v1/comercio-exterior/relacion-iva?anio=2026&mes=10"); reporte.EnsureSuccessStatusCode();
        Assert.Equal("FIX-RECTIFICADO", (await Json(reporte)).GetProperty("filas").EnumerateArray().Single(f => f.GetProperty("expediente").GetString() == dto.GetProperty("expediente").GetProperty("folio").GetString()).GetProperty("pedimento").GetString());
    }
    private static async Task<JsonElement> Json(HttpResponseMessage r) { using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return d.RootElement.Clone(); }
    private static Task<HttpResponseMessage> Send(HttpClient c, string url, object? body, string? etag, string? key = null)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, url); if (body is not null) r.Content = JsonContent.Create(body);
        r.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString()); if (etag is not null) r.Headers.TryAddWithoutValidation("If-Match", etag);
        return c.SendAsync(r);
    }
}
