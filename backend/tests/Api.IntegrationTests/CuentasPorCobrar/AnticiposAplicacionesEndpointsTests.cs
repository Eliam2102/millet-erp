using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.CuentasPorCobrar;

/// <summary>
/// F1-CXC-05: GET real de anticipos con su aplicación a factura final desde
/// Cuentas por Cobrar + query + adapter público de Facturación + PostgreSQL
/// desechable. Siembra anticipo → factura final → egreso 07 timbrado. Todos
/// los datos son ficticios.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class AnticiposAplicacionesEndpointsTests(WebApplicationFactory<Program> factory)
{
    private const string Ruta = "/api/v1/cuentas-por-cobrar/anticipos/aplicaciones";
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly DatosFiscalesReceptor Receptor = new("AAA010101AAA", "Cliente ficticio CXC-05", "601", "97000", "G03", "MEX", false);
    private static readonly DatosFiscalesEmisor Emisor = new("BBB010101BBB", "Emisor ficticio CXC-05", "601", "97000");

    [Fact]
    public async Task Devuelve_anticipo_con_su_aplicacion_y_el_uuid_del_egreso()
    {
        using var client = await Login();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();

        var clienteId = Guid.NewGuid();
        var anticipoId = Guid.CreateVersion7();
        var ahora = DateTimeOffset.UtcNow;
        var sufijo = Guid.NewGuid().ToString("N")[..8];

        var facturaAnticipo = FacturaAnticipo.CrearBorrador(Empresa, $"C05A-{sufijo}", 1, TestComprasFixtures.SucursalMid,
            null, null, Receptor, Emisor, "03", "MXN", null, 2026, 10, TipoAnticipo.ClientesMxp, null,
            anticipoId, 1000m, 0.16m, "Anticipo ficticio CXC-05", 7);
        facturaAnticipo.MarcarTimbradoEnProceso();
        facturaAnticipo.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, ahora.AddMinutes(-10), null, null);
        var anticipo = Anticipo.Crear(Empresa, clienteId, Receptor.Rfc, TipoAnticipo.ClientesMxp, "MXN",
            facturaAnticipo.Total, facturaAnticipo.Id, pedidoOrigenRef: "AW-C05", id: anticipoId);

        var facturaFinal = FacturaVenta.CrearBorrador(Empresa, $"C05F-{sufijo}", 1, TestComprasFixtures.SucursalMid,
            null, null, Receptor, Emisor, "PUE", "03", "MXN", null, 2026, 10, 7,
            ComportamientoFiscal.ConAnticipo, null, null, null, false);
        facturaFinal.MarcarTimbradoEnProceso();
        facturaFinal.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, ahora.AddMinutes(-5), null, null);

        var egreso = NotaCredito.CrearAmortizacion(Empresa, $"C05N-{sufijo}", 1, TestComprasFixtures.SucursalMid,
            null, null, Receptor, Emisor, "30", "MXN", null, 2026, 10, 7, anticipoId, facturaFinal.Id, 580m, 0.16m);
        egreso.MarcarTimbradoEnProceso();
        var uuidEgreso = Guid.NewGuid().ToString();
        egreso.MarcarTimbrado(uuidEgreso, null, null, null, ahora.AddMinutes(-4), null, null);
        anticipo.RegistrarAmortizacion(facturaFinal.Id, egreso.Id, 580m, ahora.AddMinutes(-4));

        try
        {
            db.AddRange(facturaAnticipo, facturaFinal, egreso, anticipo);
            await db.SaveChangesAsync();

            var response = await client.GetFromJsonAsync<JsonElement>($"{Ruta}?clienteId={clienteId}");

            var fila = Assert.Single(response.GetProperty("anticipos").EnumerateArray());
            Assert.Equal(anticipoId, fila.GetProperty("anticipoId").GetGuid());
            Assert.Equal(clienteId, fila.GetProperty("clienteId").GetGuid());
            Assert.Equal("Cliente ficticio CXC-05", fila.GetProperty("clienteNombre").GetString());
            Assert.Equal(facturaAnticipo.Folio, fila.GetProperty("folio").GetString());
            Assert.Equal(facturaAnticipo.Uuid, fila.GetProperty("uuid").GetString());
            Assert.StartsWith("MID", fila.GetProperty("sucursalNombre").GetString());
            Assert.Equal("MXN", fila.GetProperty("moneda").GetString());
            Assert.Equal(1160m, fila.GetProperty("cobrado").GetDecimal());
            Assert.Equal(580m, fila.GetProperty("aplicado").GetDecimal());
            Assert.Equal(580m, fila.GetProperty("saldo").GetDecimal());
            Assert.Equal(580m, fila.GetProperty("saldoSistema").GetDecimal());
            Assert.Equal("Parcial", fila.GetProperty("estado").GetString());
            Assert.Equal("AW-C05", fila.GetProperty("pedidoOrigenRef").GetString());
            Assert.Equal(JsonValueKind.Null, fila.GetProperty("observacion").ValueKind);

            var aplicacion = Assert.Single(fila.GetProperty("aplicaciones").EnumerateArray());
            Assert.Equal("Aplicación", aplicacion.GetProperty("tipo").GetString());
            Assert.Equal(facturaFinal.Folio, aplicacion.GetProperty("facturaFinalFolio").GetString());
            Assert.Equal(facturaFinal.Uuid, aplicacion.GetProperty("facturaFinalUuid").GetString());
            Assert.Equal(egreso.Folio, aplicacion.GetProperty("egresoFolio").GetString());
            Assert.Equal(uuidEgreso, aplicacion.GetProperty("egresoUuid").GetString());
            Assert.Equal("Timbrado", aplicacion.GetProperty("estadoEgreso").GetString());
            Assert.Equal(580m, aplicacion.GetProperty("importe").GetDecimal());
            Assert.True(aplicacion.TryGetProperty("fechaAplicacion", out _));

            var totales = Assert.Single(response.GetProperty("totalesPorMoneda").EnumerateArray());
            Assert.Equal("MXN", totales.GetProperty("moneda").GetString());
            Assert.Equal(1160m, totales.GetProperty("cobrado").GetDecimal());
            Assert.Equal(580m, totales.GetProperty("saldo").GetDecimal());

            // Sin cliente lista todos: el anticipo sembrado está entre ellos.
            var todos = await client.GetFromJsonAsync<JsonElement>(Ruta);
            Assert.Contains(todos.GetProperty("anticipos").EnumerateArray(),
                a => a.GetProperty("anticipoId").GetGuid() == anticipoId);

            // Filtro por estado.
            var aplicados = await client.GetFromJsonAsync<JsonElement>($"{Ruta}?clienteId={clienteId}&estado=Aplicado");
            Assert.Empty(aplicados.GetProperty("anticipos").EnumerateArray());
        }
        finally
        {
            await db.Set<AnticipoVinculacion>().Where(v => v.AnticipoId == anticipoId).ExecuteDeleteAsync();
            await db.Anticipos.Where(a => a.Id == anticipoId).ExecuteDeleteAsync();
            db.ChangeTracker.Clear();
            db.Remove(await db.NotasCredito.SingleAsync(n => n.Id == egreso.Id));
            db.Remove(await db.FacturasVenta.SingleAsync(f => f.Id == facturaFinal.Id));
            db.Remove(await db.FacturasAnticipo.SingleAsync(f => f.Id == facturaAnticipo.Id));
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Cliente_vacio_responde_400()
    {
        using var client = await Login();

        var response = await client.GetAsync($"{Ruta}?clienteId={Guid.Empty}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sin_permiso_de_cartera_responde_403()
    {
        using var client = await Login("test-no-perms");

        var response = await client.GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpClient> Login(string oid = "dev-superadmin")
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = $"{oid}@dev.local", Nombre = "Prueba CXC-05", EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }
}
