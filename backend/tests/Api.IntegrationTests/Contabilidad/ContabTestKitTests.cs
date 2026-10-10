using System.Net;
using System.Text;
using System.Text.Json;

namespace Millet.Api.IntegrationTests.Contabilidad;

public sealed class ContabTestKitTests
{
    [Theory]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Leer_una_respuesta_dos_veces_conserva_el_cuerpo(HttpStatusCode status)
    {
        const string cuerpo = "{\"solicitudId\":\"00000000-0000-0000-0000-000000000001\",\"code\":\"FIX_CONFLICTO\"}";
        using var respuesta = new HttpResponseMessage(status)
        { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

        var primera = await ContabTestKit.Json(respuesta);
        var segunda = await ContabTestKit.Json(respuesta);

        Assert.Equal(cuerpo, primera.GetRawText());
        Assert.Equal(primera.GetRawText(), segunda.GetRawText());
        Assert.Equal(cuerpo, await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Un_cuerpo_realmente_vacio_sigue_fallando()
    {
        using var respuesta = new HttpResponseMessage(HttpStatusCode.Accepted)
        { Content = new StringContent(string.Empty) };
        await Assert.ThrowsAnyAsync<JsonException>(() => ContabTestKit.Json(respuesta));
    }
}
