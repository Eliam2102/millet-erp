using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Facturacion.Application.ConciliacionSat;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.ConciliacionSat;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// FAC-10 · conciliación de CFDI emitidos contra el SAT de punta a punta contra PostgreSQL: se sube el archivo de
/// metadatos por multipart, la corrida queda guardada con sus diferencias (cancelada en el SAT y faltante en el
/// ERP) y una diferencia se marca como revisada. El periodo es de 2020 para no cruzarse con los comprobantes de
/// otras pruebas. Datos FICTICIOS.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class ConciliacionSatHttpTests(WebApplicationFactory<Program> factory)
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private const string Base = "/api/v1/facturacion/conciliacion-sat";

    [Fact]
    public async Task Concilia_el_archivo_del_sat_guarda_la_corrida_y_marca_una_diferencia_revisada()
    {
        using var client = await Login();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        // El emisor del archivo se valida contra el RFC configurado de la empresa (O1 de QA).
        var rfcEmisor = (await scope.ServiceProvider.GetRequiredService<IEmpresaFiscalReadPort>().ObtenerAsync(Empresa, default))!.Rfc.ToUpperInvariant();

        var sufijo = Guid.NewGuid().ToString("N")[..10];
        var factura = Factura(sufijo, new DateTimeOffset(2020, 1, 7, 18, 0, 0, TimeSpan.Zero), rfcEmisor);
        var ajeno = Guid.NewGuid().ToString().ToUpperInvariant();
        Guid? corridaId = null;
        try
        {
            db.Add(factura);
            await db.SaveChangesAsync();

            var archivo =
                "Uuid~RfcEmisor~NombreEmisor~RfcReceptor~NombreReceptor~RfcPac~FechaEmision~FechaCertificacionSat~Monto~EfectoComprobante~Estatus~FechaCancelacion\r\n" +
                $"{factura.Uuid}~{rfcEmisor}~EMISOR FICTICIO~AAA010101AAA~CLIENTE FICTICIO~SPR190613I52~2020-01-07 12:00:00~2020-01-07 12:00:05~116.00~I~0~2020-01-08 10:00:00\r\n" +
                $"{ajeno}~{rfcEmisor}~EMISOR FICTICIO~AAA010101AAA~CLIENTE FICTICIO~SPR190613I52~2020-01-09 12:00:00~2020-01-09 12:00:05~500.00~I~1~\r\n";
            using var form = new MultipartFormDataContent();
            var contenido = new ByteArrayContent(Encoding.UTF8.GetBytes(archivo));
            contenido.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            form.Add(contenido, "archivo", $"emitidos-{sufijo}.txt");
            form.Add(new StringContent("2020-01-06"), "desde");
            form.Add(new StringContent("2020-01-12"), "hasta");

            // Periodo vacío: 422 en español, no el error del binder (O2 de QA).
            using (var vacio = new MultipartFormDataContent())
            {
                var c2 = new ByteArrayContent(Encoding.UTF8.GetBytes(archivo));
                c2.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
                vacio.Add(c2, "archivo", $"emitidos-{sufijo}.txt");
                vacio.Add(new StringContent(""), "desde");
                vacio.Add(new StringContent(""), "hasta");
                var sinPeriodo = await client.PostAsync(Base, vacio);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, sinPeriodo.StatusCode);
                Assert.Contains("CONCILIACION_SAT_PERIODO_REQUERIDO", await sinPeriodo.Content.ReadAsStringAsync());
            }

            var r = await client.PostAsync(Base, form);
            Assert.True(r.StatusCode == HttpStatusCode.OK, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
            var corrida = (await r.Content.ReadFromJsonAsync<ConciliacionSatResponse>())!;
            corridaId = corrida.Id;
            Assert.False(corrida.SinDiferencias);
            Assert.Equal((2, 1, 2), (corrida.CfdiSat, corrida.CfdiErp, corrida.DiferenciasPendientes));

            var listado = await client.GetFromJsonAsync<List<ConciliacionSatResponse>>($"{Base}?limit=100");
            Assert.Contains(listado!, c => c.Id == corrida.Id);

            var diferencias = (await client.GetFromJsonAsync<List<DiferenciaSatResponse>>($"{Base}/{corrida.Id}/diferencias"))!;
            var estatus = Assert.Single(diferencias, d => d.Tipo == TipoDiferenciaSat.EstatusDistinto);
            Assert.Equal((factura.Id, "Cancelado", "Vigente"), (estatus.ComprobanteId!.Value, estatus.ValorSat, estatus.ValorErp));
            Assert.Equal(ajeno, Assert.Single(diferencias, d => d.Tipo == TipoDiferenciaSat.FaltanteEnErp).Uuid);

            var revisada = await client.PostAsJsonAsync($"{Base}/{corrida.Id}/diferencias/{estatus.Id}/revisada",
                new { nota = "Cancelada en el SAT; se cancela en el ERP" });
            Assert.Equal(HttpStatusCode.OK, revisada.StatusCode);
            var detalle = await client.GetFromJsonAsync<ConciliacionSatResponse>($"{Base}/{corrida.Id}");
            Assert.Equal(1, detalle!.DiferenciasPendientes);
            var otraVez = await client.PostAsJsonAsync($"{Base}/{corrida.Id}/diferencias/{estatus.Id}/revisada", new { nota = (string?)null });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, otraVez.StatusCode);
        }
        finally
        {
            db.ChangeTracker.Clear();
            if (corridaId is { } id)
            {
                await db.DiferenciasConciliacionSat.Where(d => d.ConciliacionId == id).ExecuteDeleteAsync();
                await db.ConciliacionesSat.Where(c => c.Id == id).ExecuteDeleteAsync();
            }
            db.Remove(await db.FacturasVenta.SingleAsync(f => f.Id == factura.Id));
            await db.SaveChangesAsync();
        }
    }

    private async Task<HttpClient> Login()
    {
        var client = factory.CreateClientWithIdempotency();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin", Email = "dev-superadmin@dev.local", Nombre = "SuperAdmin", EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }

    private static FacturaVenta Factura(string sufijo, DateTimeOffset timbrado, string rfcEmisor)
    {
        var f = FacturaVenta.CrearBorrador(Empresa, $"CSAT-{sufijo}", 1, TestComprasFixtures.SucursalMid, null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente ficticio conciliación", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor(rfcEmisor, "Emisor ficticio conciliación", "601", "97000"), "PUE", "03", "MXN", null,
            2020, 1, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "01010101", "Prueba ficticia de la conciliación", "H87", 1, 100, 0, "02", 0.16m, null, null);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado(Guid.NewGuid().ToString().ToUpperInvariant(), null, null, null, timbrado, null, null);
        return f;
    }
}
