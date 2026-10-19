using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// ADM-04 · La factura de venta toma el folio de la serie CFDI de SU sucursal
/// (R-ADM-11). La reserva usa el handler real de Administración; sólo el PAC,
/// el período y el emisor fiscal son dobles. Las reglas de la serie (unicidad,
/// concurrencia, fallback global) se prueban en <c>SeriesEndpointsTests</c>.
/// </summary>
public sealed class SerieEnEmisionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid EmpresaBootstrapId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private readonly WebApplicationFactory<Program> _factory;
    public SerieEnEmisionTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Factura_de_cada_sucursal_usa_el_prefijo_de_su_serie_CFDI()
    {
        await using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICfdiTimbradoPort>();
            services.AddSingleton<ICfdiTimbradoPort, PacSinTimbre>();
            services.RemoveAll<IPeriodoContablePort>();
            services.AddSingleton<IPeriodoContablePort, PeriodoAbierto>();
            services.RemoveAll<IEmpresaFiscalReadPort>();
            services.AddSingleton<IEmpresaFiscalReadPort, EmisorPrueba>();
        }));
        using var client = factory.CreateClientWithIdempotency();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client));

        var sucursalA = await CrearSucursalAsync(client, "A04A");
        var sucursalB = await CrearSucursalAsync(client, "A04B");
        var prefijoA = Prefijo();
        var prefijoB = Prefijo();
        await CrearSerieCfdiAsync(client, sucursalA, prefijoA);
        await CrearSerieCfdiAsync(client, sucursalB, prefijoB);

        var folioA = await EmitirYLeerFolioAsync(factory, client, sucursalA);
        var folioB = await EmitirYLeerFolioAsync(factory, client, sucursalB);

        Assert.StartsWith(prefijoA, folioA);
        Assert.StartsWith(prefijoB, folioB);
        Assert.DoesNotContain(prefijoB, folioA);
    }

    private static async Task<string> EmitirYLeerFolioAsync(
        WebApplicationFactory<Program> factory, HttpClient client, Guid sucursalId)
    {
        var command = new EmitirFacturaVentaCommand(
            sucursalId, "XAXX010101000", "Público en general", "616", "97000", "S01", "MEX",
            "AAA010101AAA", "601", "PUE", "01", "MXN", null, 1,
            ComportamientoFiscal.MostradorInmediato, null, null, false,
            [new(null, "01010101", "ADM-04 vidrio de prueba", "H87", 1, 1000, 0, "02", 0.16m, null, null)]);
        var response = await client.PostAsJsonAsync("/api/v1/facturacion/facturas/", command);
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"Emisión ADM-04: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        var id = (await response.Content.ReadFromJsonAsync<EmitirFacturaVentaResponse>())!.Id;

        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var factura = await scope.ServiceProvider.GetRequiredService<FacturacionDbContext>()
            .FacturasVenta.AsNoTracking().SingleAsync(f => f.Id == id);
        Assert.Equal(sucursalId, factura.SucursalId);
        return factura.Folio;
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin", Email = "dev-superadmin@dev.local",
            Nombre = "SuperAdmin", EmpresaId = (Guid?)null,
        });
        login.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static async Task<Guid> CrearSucursalAsync(HttpClient client, string prefix)
    {
        var clave = $"{prefix}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var response = await client.PostAsJsonAsync("/api/v1/admin/empresas/sucursales",
            new { Id = Guid.Empty, Clave = clave, Nombre = $"Sucursal {clave}" });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task CrearSerieCfdiAsync(HttpClient client, Guid sucursalId, string prefijo)
    {
        var response = await client.PostAsJsonAsync("/api/v1/admin/series", new
        {
            Id = Guid.Empty, EmpresaId = EmpresaBootstrapId, SucursalId = sucursalId,
            TipoDocumento = 2, Prefijo = prefijo, Sufijo = (string?)null, ReinicioPeriodo = 0,
        });
        Assert.True(response.IsSuccessStatusCode,
            $"Serie ADM-04: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    // Máx. 10 caracteres; aleatorio para no chocar con datos de otras corridas.
    private static string Prefijo() => "F" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant();

    private sealed class PacSinTimbre : ICfdiTimbradoPort
    {
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct) =>
            Task.FromResult(new TimbradoResultado(TimbradoEstado.Fallido,
                null, null, null, null, null, null, null, "ADM04", "Sin PAC en la prueba"));
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud solicitud, CancellationToken ct) => throw new NotSupportedException();
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud solicitud, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class PeriodoAbierto : IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class EmisorPrueba : IEmpresaFiscalReadPort
    {
        public Task<EmpresaFiscalLectura?> ObtenerAsync(Guid empresaId, CancellationToken ct) =>
            Task.FromResult<EmpresaFiscalLectura?>(new(empresaId, "AAA010101AAA", "ADM-04 emisor", "601", 0.16m, "97000"));
        public Task<Guid?> ObtenerSucursalUnicaActivaAsync(CancellationToken ct) => Task.FromResult<Guid?>(TestComprasFixtures.SucursalMid);
    }
}
