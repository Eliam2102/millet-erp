using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Millet.Api.IntegrationTests.ComercioExterior;

/// <summary>
/// M-373: corregir la fecha real o la omisión de un hito (motivo, historial, permiso propio y cierre
/// automático del aviso de atraso) y requisito condicional configurable. Lo corre CI (PostgreSQL aislado).
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class M373CorregirHitoHttpTests
{
    private const string Base = "/api/v1/comercio-exterior/expedientes";
    private static readonly DateOnly Compromiso = new(2026, 10, 20);
    private static readonly DateOnly Capturada = new(2026, 10, 22);

    private static async Task<HttpResponseMessage> Post(HttpClient client, Guid id, string ruta, object body, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{id}/{ruta}") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static JsonElement Hito(JsonElement detalle, string clave) => detalle.GetProperty("hitos").EnumerateArray().Single(h => h.GetProperty("clave").GetString() == clave);

    [Fact]
    public async Task Corregir_hito_con_motivo_deja_historial_cierra_el_aviso_de_atraso_y_exige_su_permiso()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var client = await e.ClienteAsync(true);
        using var sinPermiso = await e.ClienteAsync(true, corregirHitos: false);
        var creado = await client.PostAsJsonAsync(Base, e.Importacion()); creado.EnsureSuccessStatusCode();
        var id = (await Json(creado)).GetProperty("expediente").GetProperty("id").GetGuid();
        var compromiso = await Post(client, id, "hitos/planeado/compromiso", new { clave = "planeado", fechaCompromiso = Compromiso, responsableUsuarioId = e.Responsable }, creado.Headers.ETag!.Tag);
        compromiso.EnsureSuccessStatusCode();
        await e.CorrerAvisosAsync();
        var real = await Post(client, id, "hitos/planeado/real", new { clave = "planeado", fechaReal = Capturada, responsableUsuarioId = e.Responsable }, (await client.GetAsync($"{Base}/{id}")).Headers.ETag!.Tag);
        real.EnsureSuccessStatusCode();
        var aviso = (await Json(real)).GetProperty("avisos").EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, aviso.GetProperty("cerradoEn").ValueKind);
        var etag = real.Headers.ETag!.Tag;

        // CA2: sin motivo se rechaza y el hito no cambia.
        var sinMotivo = await Post(client, id, "hitos/planeado/corregir", new { fechaReal = Compromiso }, etag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinMotivo.StatusCode);
        Assert.Equal("CE_MOTIVO_CORRECCION_REQUERIDO", (await Json(sinMotivo)).GetProperty("code").GetString());
        // Un rol sin el permiso de corrección (aunque registre hitos) no puede corregir.
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(sinPermiso, id, "hitos/planeado/corregir", new { fechaReal = Compromiso, motivo = "Se capturó la fecha del correo" }, etag)).StatusCode);

        // CA1 + CA4: 22-oct → 20-oct con motivo; historial con usuario y motivo; el aviso de atraso se cierra solo.
        var corregido = await Post(client, id, "hitos/planeado/corregir", new { fechaReal = Compromiso, motivo = "Se capturó la fecha del correo, no la del booking" }, etag);
        Assert.Equal(HttpStatusCode.OK, corregido.StatusCode);
        var detalle = await Json(corregido);
        var hito = Hito(detalle, "planeado");
        Assert.Equal("2026-10-20", hito.GetProperty("fechaReal").GetString());
        var historial = hito.GetProperty("correcciones").EnumerateArray().Single();
        Assert.Equal("2026-10-22", historial.GetProperty("fechaRealAnterior").GetString());
        Assert.Equal("2026-10-20", historial.GetProperty("fechaRealNueva").GetString());
        Assert.Equal("Se capturó la fecha del correo, no la del booking", historial.GetProperty("motivo").GetString());
        Assert.Equal("FIX-CE usuario", historial.GetProperty("usuarioNombre").GetString());
        Assert.NotEqual(Guid.Empty, historial.GetProperty("usuarioId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, detalle.GetProperty("avisos").EnumerateArray().Single().GetProperty("cerradoEn").ValueKind);
        Assert.Equal(0, detalle.GetProperty("expediente").GetProperty("avisos").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(client, id, "hitos/planeado/corregir", new { fechaReal = Capturada, motivo = "Reintento con etag viejo" }, etag)).StatusCode);

        // Una corrección que vuelve a dejar atraso reabre el aviso.
        var reabre = await Post(client, id, "hitos/planeado/corregir", new { fechaReal = Capturada, motivo = "La fecha correcta sí era la del 22" }, corregido.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.OK, reabre.StatusCode);
        var final = await Json(reabre);
        Assert.Equal(JsonValueKind.Null, final.GetProperty("avisos").EnumerateArray().Single().GetProperty("cerradoEn").ValueKind);
        Assert.Equal(2, Hito(final, "planeado").GetProperty("correcciones").GetArrayLength());
    }

    [Fact]
    public async Task Requisito_condicional_se_configura_por_tipo_de_documento_sin_cambiar_codigo()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var client = await e.ClienteAsync(true);
        var creado = await client.PostAsJsonAsync(Base, e.Importacion()); creado.EnsureSuccessStatusCode();
        var detalle = await Json(creado);
        var id = detalle.GetProperty("expediente").GetProperty("id").GetGuid();
        var requisitos = detalle.GetProperty("requisitos").EnumerateArray().ToArray();
        // La migración conserva los condicionales que estaban fijos en código.
        Assert.True(requisitos.Single(r => r.GetProperty("codigo").GetString() == "IMP_REGULATORIOS").GetProperty("condicional").GetBoolean());
        var oc = requisitos.Single(r => r.GetProperty("codigo").GetString() == "IMP_OC");
        Assert.False(oc.GetProperty("condicional").GetBoolean());
        var ocId = oc.GetProperty("id").GetGuid();
        var noAplica = new { tipoDocumentoId = ocId, adjuntoId = (Guid?)null, noAplica = true, motivo = "La operación llegó sin orden de compra física" };

        var rechazo = await Post(client, id, "documentos", noAplica, creado.Headers.ETag!.Tag);
        Assert.Equal("CE_REQUISITO_NO_CONDICIONAL", (await Json(rechazo)).GetProperty("code").GetString());
        using var lectura = await e.ClienteAsync(true, soloLeer: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(lectura, id, "requisito-condicional", new { tipoDocumentoId = ocId, condicional = true }, creado.Headers.ETag!.Tag)).StatusCode);

        var config = await Post(client, id, "requisito-condicional", new { tipoDocumentoId = ocId, condicional = true }, creado.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.OK, config.StatusCode);
        Assert.True((await Json(config)).GetProperty("requisitos").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == ocId).GetProperty("condicional").GetBoolean());

        var aceptado = await Post(client, id, "documentos", noAplica, config.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.OK, aceptado.StatusCode);
        Assert.DoesNotContain((await Json(aceptado)).GetProperty("faltantes").EnumerateArray(), r => r.GetProperty("id").GetGuid() == ocId);
    }
}
