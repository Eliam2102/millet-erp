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

public sealed class ReceptorFiscalEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid EmpresaId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Maestro_sin_cp_devuelve_422_antes_de_folio_PAC_y_sustitucion_sandbox(bool sandbox)
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
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var facturacion = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var cliente = new Cliente(clienteId, "U19-" + clienteId.ToString("N")[..12], "CLIENTE FICTICIO U1.9",
            rfc: "AAA010101AAA", regimenFiscal: "601", codigoPostalFiscal: null, usoCfdiDefault: "G03");
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

            // Snapshot completo: el maestro incompleto debe bloquear aunque el caller lo suplante.
            var command = new EmitirFacturaVentaCommand(sucursal, "AAA010101AAA", "CLIENTE FICTICIO U1.9", "601", "97000", "G03", "MEX",
                "MIL010101AAA", "601", "PUE", "01", "MXN", null, 1, ComportamientoFiscal.Administrativa,
                null, null, false, [new(null, "01010101", "Prueba U1.9", "H87", 1, 100, 0, "02", 0.16m, null, null)], ClienteId: clienteId);
            var response = await client.PostAsJsonAsync("/api/v1/facturacion/facturas/", command);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var problem = json.RootElement;
            Assert.Equal("RECEPTOR_FISCAL_INVALIDO", problem.GetProperty("code").GetString());
            Assert.Equal(clienteId, problem.GetProperty("clienteId").GetGuid());
            Assert.Equal($"/admin/datos-maestros/clientes/{clienteId}", problem.GetProperty("enlaceCliente").GetString());
            Assert.Contains(problem.GetProperty("campos").EnumerateArray(), c => c.GetProperty("campo").GetString() == "codigoPostalFiscal");
            Assert.Contains("CP fiscal", problem.GetProperty("detail").GetString());
            Assert.Equal(0, contador.Intentos);
            Assert.Equal(foliosAntes, await db.SecuenciasFolio.AsNoTracking().OrderBy(s => s.Id).Select(s => new { s.Id, s.UltimoNumero }).ToListAsync());
            Assert.Equal(intentosAntes, await facturacion.BitacorasIntentoTimbrado.CountAsync());
            Assert.Equal(documentosAntes, await facturacion.Comprobantes.CountAsync());
        }
        finally
        {
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
