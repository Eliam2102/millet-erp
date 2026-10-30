using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.ComercioExterior;

/// <summary>K10.6-CE: tipo de cambio editable del gasto en USD. Lo corre CI (PostgreSQL aislado).</summary>
[Collection(ApiCompartida.Nombre)]
public sealed class TipoCambioGastoHttpTests
{
    private const string Base = "/api/v1/comercio-exterior/expedientes";
    private static async Task<HttpResponseMessage> Post(HttpClient client, Guid id, string ruta, object body, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{id}/{ruta}") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
        return await client.SendAsync(request);
    }
    [Fact]
    public async Task CA1_a_CA4_banxico_por_omision_negociado_con_nota_y_rechazo_sin_nota()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync(); using var client = await e.ClienteAsync(true);
        var creado = await client.PostAsJsonAsync(Base, e.Importacion()); creado.EnsureSuccessStatusCode();
        var id = (await creado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expediente").GetProperty("id").GetGuid();
        var inicial = await Post(client, id, "gastos/conceptos/inicializar", new { }, creado.Headers.ETag!.Tag); inicial.EnsureSuccessStatusCode();
        var conceptos = (await inicial.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("conceptos");
        var concepto = conceptos.EnumerateArray().First(c => c.GetProperty("codigo").GetString() == "FLETE_INT").GetProperty("id").GetGuid();
        Guid proveedor;
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var maestros = scope.ServiceProvider.GetRequiredService<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>();
            proveedor = await maestros.Proveedores.Where(p => p.Clave == "DEMO-PROV-ACT").Select(p => p.Id).SingleAsync();
            var usd = await maestros.Monedas.SingleAsync(m => m.Codigo == "USD");
            var fecha = new DateOnly(2026, 10, 21);
            if (!await maestros.TiposCambio.AnyAsync(t => t.MonedaId == usd.Id && t.Fecha == fecha && t.Origen == OrigenTipoCambio.Banxico))
            {
                maestros.TiposCambio.Add(new TipoCambio(Guid.CreateVersion7(), usd.Id, fecha, 18.4253m, OrigenTipoCambio.Banxico));
                await maestros.SaveChangesAsync();
            }
        }
        object Gasto(string factura, object? extra = null) => new Dictionary<string, object?>
        {
            ["conceptoId"] = concepto, ["proveedorId"] = proveedor, ["factura"] = factura, ["moneda"] = "USD", ["importe"] = 1000,
            ["fechaFactura"] = "2026-10-21",
        }.Concat(extra is null ? [] : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(extra))!).ToDictionary(x => x.Key, x => x.Value);
        var etag = inicial.Headers.ETag!.Tag;
        var sinCambio = await Post(client, id, "gastos", Gasto("TC-1"), etag); sinCambio.EnsureSuccessStatusCode();
        var g1 = (await sinCambio.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("gastos").EnumerateArray().Single();
        Assert.Equal(18425.30m, g1.GetProperty("importeMxn").GetDecimal()); Assert.Equal("Banxico", g1.GetProperty("fuenteTipoCambio").GetString());
        etag = sinCambio.Headers.ETag!.Tag;
        var sinNota = await Post(client, id, "gastos", Gasto("TC-2", new { fuenteTipoCambio = "Negociado", tipoCambioUsado = 18.30m }), etag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinNota.StatusCode);
        var conNota = await Post(client, id, "gastos", Gasto("TC-2", new { fuenteTipoCambio = "Negociado", tipoCambioUsado = 18.30m, notaTipoCambio = "Pactado con el agente" }), etag);
        conNota.EnsureSuccessStatusCode();
        var g2 = (await conNota.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("gastos").EnumerateArray().Single(g => g.GetProperty("factura").GetString() == "TC-2");
        Assert.Equal(18300.00m, g2.GetProperty("importeMxn").GetDecimal()); Assert.Equal(18.4253m, g2.GetProperty("tipoCambioSugerido").GetDecimal());
        Assert.Equal(-0.68m, g2.GetProperty("diferenciaTipoCambioPct").GetDecimal()); Assert.NotEqual(Guid.Empty, g2.GetProperty("tipoCambioModificadoPor").GetGuid());
    }
}
