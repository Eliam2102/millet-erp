using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

public sealed class ProductoFiscalF1AEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid EmpresaId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    // RFC no genérico y único por ejecución: ux_clientes_rfc_no_generico impide duplicados en la BD compartida.
    private static string NuevoRfc() => "ZZZ" + Random.Shared.Next(0, 1_000_000).ToString("D6") + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Producto_sin_ficha_devuelve_422_antes_de_folio_PAC_y_sustitucion_sandbox(bool sandbox)
    {
        var contador = new Contador();
        var configuracion = new ConfiguracionPrueba(sandbox);
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICfdiTimbradoPort>();
            services.RemoveAll<IConfiguracionPacResolver>();
            services.RemoveAll<IFiscalApiSdkClientFactory>();
            services.RemoveAll<IEmpresaFiscalReadPort>();
            services.RemoveAll<IPeriodoContablePort>();
            services.AddSingleton<IConfiguracionPacResolver>(configuracion);
            services.AddSingleton<IFiscalApiSdkClientFactory>(new SdkProhibido());
            services.AddSingleton<IEmpresaFiscalReadPort>(new EmisorPrueba());
            services.AddSingleton<IPeriodoContablePort>(new PeriodoAbierto());
            services.AddScoped<FiscalApiTimbradoAdapter>();
            services.AddScoped<ICfdiTimbradoPort>(sp => new PacContador(sp.GetRequiredService<FiscalApiTimbradoAdapter>(), contador));
        }));
        using var client = app.CreateClientWithIdempotency();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin", Email = "superadmin@dev.local", Nombre = "SuperAdmin", EmpresaId,
        });
        login.EnsureSuccessStatusCode();
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginJson.RootElement.GetProperty("accessToken").GetString());

        var clienteId = Guid.NewGuid();
        var productoId = Guid.NewGuid();
        var rfc = NuevoRfc();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var facturacion = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var cliente = new Cliente(clienteId, "U19-" + clienteId.ToString("N")[..12], "CLIENTE FICTICIO U1.9",
            rfc: rfc, regimenFiscal: "601", codigoPostalFiscal: "97000", usoCfdiDefault: "G03");
        var producto = new Millet.DatosMaestros.Domain.ProductoAw(productoId, "F1A-" + productoId.ToString("N")[..12], "PROCESO FICTICIO F1A", "PZA");
        db.ProductosAw.Add(producto);
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        try
        {
            var sucursal = await db.Sucursales.Select(s => s.Id).FirstAsync();
            var foliosAntes = await db.SecuenciasFolio.AsNoTracking().OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.UltimoNumero }).ToListAsync();
            var intentosAntes = await facturacion.BitacorasIntentoTimbrado.CountAsync();
            var documentosAntes = await facturacion.Comprobantes.CountAsync();
            var config = await configuracion.ResolverAsync(EmpresaId, ProveedorPac.FiscalApi, CancellationToken.None);
            Assert.Equal(sandbox, config!.ReceptorSandbox is not null);

            // Concepto completo en el request: la ficha incompleta del maestro debe bloquear aunque se suplanten las claves.
            var command = new EmitirFacturaVentaCommand(sucursal, rfc, "CLIENTE FICTICIO U1.9", "601", "97000", "G03", "MEX",
                "MIL010101AAA", "601", "PUE", "01", "MXN", null, 1, ComportamientoFiscal.Administrativa,
                null, null, false, [new(productoId, "01010101", "Prueba U1.9b", "H87", 1, 100, 0, "02", 0.16m, null, null)], ClienteId: clienteId);
            var response = await client.PostAsJsonAsync("/api/v1/facturacion/facturas/", command);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var problem = json.RootElement;
            Assert.Equal("PRODUCTOS_FISCALES_INCOMPLETOS", problem.GetProperty("code").GetString());
            Assert.Equal(productoId, problem.GetProperty("productos")[0].GetProperty("productoId").GetGuid());
            Assert.Equal(4, problem.GetProperty("productos")[0].GetProperty("campos").GetArrayLength());
            Assert.Contains("PROCESO FICTICIO F1A", problem.GetProperty("detail").GetString());
            Assert.Equal(0, contador.Intentos);
            Assert.Equal(foliosAntes, await db.SecuenciasFolio.AsNoTracking().OrderBy(s => s.Id).Select(s => new { s.Id, s.UltimoNumero }).ToListAsync());
            Assert.Equal(intentosAntes, await facturacion.BitacorasIntentoTimbrado.CountAsync());
            Assert.Equal(documentosAntes, await facturacion.Comprobantes.CountAsync());
        }
        finally
        {
            db.ProductosAw.Remove(producto);
            db.Clientes.Remove(cliente);
            await db.SaveChangesAsync();
        }
    }

    private sealed class Contador { public int Intentos { get; set; } }
    private sealed class PacContador(ICfdiTimbradoPort inner, Contador contador) : ICfdiTimbradoPort
    {
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct)
        {
            contador.Intentos++;
            return inner.TimbrarAsync(emision, ct);
        }
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud solicitud, CancellationToken ct) => inner.CancelarAsync(solicitud, ct);
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud solicitud, CancellationToken ct) => inner.ConsultarEstatusAsync(solicitud, ct);
    }
    private sealed class SdkProhibido : IFiscalApiSdkClientFactory
    {
        public Task<Fiscalapi.Abstractions.IFiscalApiClient> GetClientAsync(Guid empresaId, CancellationToken ct) =>
            throw new InvalidOperationException("La validación fiscal debe detenerse antes de entrar al adaptador PAC.");
    }
    private sealed class ConfiguracionPrueba(bool sandbox) : IConfiguracionPacResolver
    {
        public Task<ConfiguracionPacResuelta?> ResolverAsync(Guid empresaId, ProveedorPac proveedor, CancellationToken ct) =>
            Task.FromResult<ConfiguracionPacResuelta?>(new(Guid.NewGuid(), empresaId, proveedor,
                sandbox ? "https://test.fiscalapi.com" : "https://live.fiscalapi.com", "ficticia-u19", true,
                EmisorSandbox: sandbox ? new("EKU9003173C9", "ESCUELA KEMPER URGATE", "601", "42501") : null,
                ReceptorSandbox: sandbox ? new("URE180429TM6", "UNIVERSIDAD ROBOTICA ESPAÑOLA", "601", "65000") : null,
                Csd: new("ficticio", "ficticio", "ficticio")));
        public void Invalidar(Guid empresaId, ProveedorPac proveedor) { }
    }
    private sealed class EmisorPrueba : IEmpresaFiscalReadPort
    {
        public Task<Guid?> ObtenerSucursalUnicaActivaAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);
        public Task<EmpresaFiscalLectura?> ObtenerAsync(Guid empresaId, CancellationToken ct) =>
            Task.FromResult<EmpresaFiscalLectura?>(new(empresaId, "MIL010101AAA", "Millet prueba", "601", 0.16m, "97000"));
    }
    private sealed class PeriodoAbierto : IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int anio, int mes, CancellationToken ct) => Task.FromResult(true);
    }
}
