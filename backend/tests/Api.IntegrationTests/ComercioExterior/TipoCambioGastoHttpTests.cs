using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Seed;
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
        var facturas = new Dictionary<string, Guid>();
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var cxp = scope.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext>();
            var fecha = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
            foreach (var folio in new[] { "TC-1", "TC-2", "TC-BX", "TC-MXN" })
            {
                var factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor.CapturarSinOc(DemoSesionSeedHostedService.EmpresaId, null, Guid.NewGuid().ToString(),
                    proveedor, e.SucursalId, folio, null, fecha, fecha, new DateOnly(2026, 11, 21), "USD", 18.35m, 1000, 0, 0, 0, 1000, "P22 preparación P19", fecha);
                cxp.FacturasProveedor.Add(factura); facturas.Add(folio, factura.Id);
            }
            await cxp.SaveChangesAsync();
        }
        object Gasto(string factura, object? extra = null) => new Dictionary<string, object?>
        {
            ["conceptoId"] = concepto, ["proveedorId"] = proveedor, ["factura"] = factura, ["moneda"] = "USD", ["importe"] = 1000,
            ["fechaFactura"] = "2026-10-21", ["facturaProveedorId"] = facturas.GetValueOrDefault(factura),
        }.Concat(extra is null ? [] : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(extra))!).GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.Last().Value);
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
        // CA3: factura de CxP con TC 18.35; «CFDI» sin capturar el valor toma el de la factura.
        Guid facturaId;
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var cxp = scope.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext>();
            var fecha = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
            var factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor.CapturarSinOc(DemoSesionSeedHostedService.EmpresaId, null, Guid.NewGuid().ToString(),
                proveedor, e.SucursalId, "TC-CFDI", null, fecha, fecha, new DateOnly(2026, 11, 21), "USD", 18.35m, 1000, 0, 0, 0, 1000, "K10.6-CE prueba", fecha);
            cxp.Set<Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor>().Add(factura);
            await cxp.SaveChangesAsync(); facturaId = factura.Id;
        }
        var cfdi = await Post(client, id, "gastos", Gasto("TC-CFDI", new { fuenteTipoCambio = "CFDI", notaTipoCambio = "TC del CFDI de la factura", facturaProveedorId = facturaId }), conNota.Headers.ETag!.Tag);
        cfdi.EnsureSuccessStatusCode();
        var g3 = (await cfdi.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("gastos").EnumerateArray().Single(g => g.GetProperty("factura").GetString() == "TC-CFDI");
        Assert.Equal(18350.00m, g3.GetProperty("importeMxn").GetDecimal()); Assert.Equal("CFDI", g3.GetProperty("fuenteTipoCambio").GetString());
        Assert.Equal(18.35m, g3.GetProperty("tipoCambio").GetDecimal());
        // Hardening: «Banxico» ignora el TC enviado; «MXN» en un gasto USD se rechaza.
        var banxicoFalso = await Post(client, id, "gastos", Gasto("TC-BX", new { fuenteTipoCambio = "Banxico", tipoCambioUsado = 17.00m }), cfdi.Headers.ETag!.Tag);
        banxicoFalso.EnsureSuccessStatusCode();
        var g4 = (await banxicoFalso.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("gastos").EnumerateArray().Single(g => g.GetProperty("factura").GetString() == "TC-BX");
        Assert.Equal(18.4253m, g4.GetProperty("tipoCambio").GetDecimal()); Assert.Equal(18425.30m, g4.GetProperty("importeMxn").GetDecimal());
        var mxnEnUsd = await Post(client, id, "gastos", Gasto("TC-MXN", new { fuenteTipoCambio = "MXN", tipoCambioUsado = 5m }), banxicoFalso.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mxnEnUsd.StatusCode);
        Assert.Equal("CE_TIPO_CAMBIO_FUENTE_INVALIDA", (await mxnEnUsd.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task M187_fecha_con_tc_manual_o_sin_tc_acepta_el_capturado_y_conserva_el_error_de_banxico()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync(); using var client = await e.ClienteAsync(true);
        var creado = await client.PostAsJsonAsync(Base, e.Importacion()); creado.EnsureSuccessStatusCode();
        var id = (await creado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expediente").GetProperty("id").GetGuid();
        var inicial = await Post(client, id, "gastos/conceptos/inicializar", new { }, creado.Headers.ETag!.Tag); inicial.EnsureSuccessStatusCode();
        var concepto = (await inicial.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("conceptos").EnumerateArray()
            .First(c => c.GetProperty("codigo").GetString() == "FLETE_INT").GetProperty("id").GetGuid();
        var conManual = new DateOnly(2026, 10, 4); var sinFila = new DateOnly(2026, 10, 3);
        Guid proveedor;
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var maestros = scope.ServiceProvider.GetRequiredService<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>();
            proveedor = await maestros.Proveedores.Where(p => p.Clave == "DEMO-PROV-ACT").Select(p => p.Id).SingleAsync();
            var usd = await maestros.Monedas.SingleAsync(m => m.Codigo == "USD");
            maestros.TiposCambio.RemoveRange(await maestros.TiposCambio.Where(t => t.MonedaId == usd.Id && (t.Fecha == conManual || t.Fecha == sinFila)).ToListAsync());
            maestros.TiposCambio.Add(new TipoCambio(Guid.CreateVersion7(), usd.Id, conManual, 20.1234m, OrigenTipoCambio.Manual));
            await maestros.SaveChangesAsync();
        }
        var facturas = new Dictionary<string, Guid>();
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var cxp = scope.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext>();
            foreach (var (folio, dia) in new[] { ("M187-CAT", conManual), ("M187-NEG", conManual), ("M187-SIN", sinFila), ("M187-BX", sinFila) })
            {
                var fecha = new DateTimeOffset(dia.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
                var factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor.CapturarSinOc(DemoSesionSeedHostedService.EmpresaId, null, Guid.NewGuid().ToString(),
                    proveedor, e.SucursalId, folio, null, fecha, fecha, dia.AddMonths(1), "USD", null, 1000, 0, 0, 0, 1000, "M-187 prueba", fecha);
                cxp.FacturasProveedor.Add(factura); facturas.Add(folio, factura.Id);
            }
            await cxp.SaveChangesAsync();
        }
        object Gasto(string factura, DateOnly fecha, object? extra = null) => new Dictionary<string, object?>
        {
            ["conceptoId"] = concepto, ["proveedorId"] = proveedor, ["factura"] = factura, ["moneda"] = "USD", ["importe"] = 1000,
            ["fechaFactura"] = fecha.ToString("yyyy-MM-dd"), ["facturaProveedorId"] = facturas[factura],
        }.Concat(extra is null ? [] : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(extra))!).GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.Last().Value);
        static async Task<JsonElement> Leer(HttpResponseMessage r, string factura) =>
            (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("gastos").EnumerateArray().Single(g => g.GetProperty("factura").GetString() == factura);

        // Rama 2: sin Banxico en la fecha, usa la fila Manual del catálogo y muestra su origen.
        var catalogo = await Post(client, id, "gastos", Gasto("M187-CAT", conManual), inicial.Headers.ETag!.Tag); catalogo.EnsureSuccessStatusCode();
        var g1 = await Leer(catalogo, "M187-CAT");
        Assert.Equal("Catálogo Manual", g1.GetProperty("fuenteTipoCambio").GetString()); Assert.Equal(20123.40m, g1.GetProperty("importeMxn").GetDecimal());
        // Rama 1: tipo de cambio capturado con fila Manual en la fecha (antes fallaba) y sin ninguna fila.
        var negociado = await Post(client, id, "gastos", Gasto("M187-NEG", conManual, new { fuenteTipoCambio = "Negociado", tipoCambioUsado = 20.00m, notaTipoCambio = "Pactado con el agente" }), catalogo.Headers.ETag!.Tag);
        negociado.EnsureSuccessStatusCode();
        var g2 = await Leer(negociado, "M187-NEG");
        Assert.Equal(20000.00m, g2.GetProperty("importeMxn").GetDecimal()); Assert.Equal(20.1234m, g2.GetProperty("tipoCambioSugerido").GetDecimal());
        var sin = await Post(client, id, "gastos", Gasto("M187-SIN", sinFila, new { fuenteTipoCambio = "Pedimento", tipoCambioUsado = 19.50m, notaTipoCambio = "TC del pedimento de importación" }), negociado.Headers.ETag!.Tag);
        sin.EnsureSuccessStatusCode();
        var g3 = await Leer(sin, "M187-SIN");
        Assert.Equal(19500.00m, g3.GetProperty("importeMxn").GetDecimal()); Assert.Equal(JsonValueKind.Null, g3.GetProperty("tipoCambioSugerido").ValueKind);
        // Rama 3: fuente Banxico sin ninguna fila en la fecha conserva el error actual.
        var banxico = await Post(client, id, "gastos", Gasto("M187-BX", sinFila), sin.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, banxico.StatusCode);
        Assert.Equal("CE_TC_BANXICO_FALTANTE", (await banxico.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
