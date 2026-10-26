using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Application.Reportes.CierreDia;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// F6.7: endpoints reales del cierre del día por sucursal (monitor de CFDI,
/// verificación de estatus SAT con el adaptador simulado y conciliación
/// facturado contra ingresado) sobre PostgreSQL desechable. La sucursal es
/// compartida con otras suites: se busca la factura propia, no se cuentan filas.
/// Datos ficticios.
/// </summary>
public sealed class CierreDiaEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Base = "/api/v1/facturacion/reportes";
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly DatosFiscalesReceptor Receptor = new("AAA010101AAA", "Cliente ficticio F6.7", "601", "97000", "G03", "MEX", false);
    private static readonly DatosFiscalesEmisor Emisor = new("BBB010101BBB", "Emisor ficticio F6.7", "601", "97000");

    [Fact]
    public async Task Monitor_verificacion_SAT_y_conciliacion_de_una_PUE_sin_cobro()
    {
        using var client = await Login();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var zona = await scope.ServiceProvider.GetRequiredService<ISucursalesReadPort>()
            .ObtenerZonaHorariaAsync(TestComprasFixtures.SucursalMid, default);
        var ahora = DateTimeOffset.UtcNow;
        var dia = DiaOperacion.HoyLocal(ahora, zona).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var factura = Factura(ahora);
        var query = $"sucursalId={TestComprasFixtures.SucursalMid}&dia={dia}";
        try
        {
            db.Add(factura);
            await db.SaveChangesAsync();

            var monitor = (await client.GetFromJsonAsync<MonitorCfdiResponse>($"{Base}/monitor-cfdi?{query}"))!;
            var fila = Assert.Single(monitor.Comprobantes.Filas, f => f.ComprobanteId == factura.Id);
            Assert.Equal("Timbrado", fila.Estado);
            Assert.Equal("Sin verificar", fila.EstatusSat);
            Assert.Contains("Simulado", monitor.FuenteSat);

            using var verificar = new HttpRequestMessage(HttpMethod.Post, $"{Base}/monitor-cfdi/verificar-sat?{query}");
            verificar.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            var respuesta = await client.SendAsync(verificar);
            respuesta.EnsureSuccessStatusCode();
            var resultado = (await respuesta.Content.ReadFromJsonAsync<VerificarEstatusSatResponse>())!;
            Assert.True(resultado.Consultados >= 1);

            monitor = (await client.GetFromJsonAsync<MonitorCfdiResponse>($"{Base}/monitor-cfdi?{query}"))!;
            fila = Assert.Single(monitor.Comprobantes.Filas, f => f.ComprobanteId == factura.Id);
            Assert.Equal(EstatusSat.Vigente, fila.EstatusSat);
            Assert.False(fila.Discrepancia);

            var conciliacion = (await client.GetFromJsonAsync<ConciliacionFacturadoIngresadoResponse>(
                $"{Base}/conciliacion-facturado-ingresado?{query}"))!;
            var partida = Assert.Single(conciliacion.Partidas.Filas, p => p.ComprobanteId == factura.Id);
            Assert.Equal(ConciliacionFacturadoIngresadoHandler.FacturadaSinCobro, partida.Situacion);
            Assert.Equal(factura.Total, partida.Diferencia);
            Assert.False(conciliacion.Resumen.Cuadra);
        }
        finally
        {
            db.ChangeTracker.Clear();
            db.Remove(await db.FacturasVenta.SingleAsync(f => f.Id == factura.Id));
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Verificar_SAT_sin_Idempotency_Key_responde_400()
    {
        using var client = await Login();

        var response = await client.PostAsync(
            $"{Base}/monitor-cfdi/verificar-sat?sucursalId={TestComprasFixtures.SucursalMid}&dia=2026-10-23", null);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpClient> Login()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin", Email = "dev-superadmin@dev.local", Nombre = "SuperAdmin", EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }

    private static FacturaVenta Factura(DateTimeOffset timbrada)
    {
        var f = FacturaVenta.CrearBorrador(Empresa, $"F67-{Guid.NewGuid():N}", 1, TestComprasFixtures.SucursalMid,
            null, null, Receptor, Emisor, "PUE", "01", "MXN", null,
            2026, 10, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "01010101", "Prueba ficticia F6.7", "H87", 1, 100, 0, "02", 0.16m, null, null);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, timbrada, null, null);
        return f;
    }
}
