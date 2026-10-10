using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Administracion.Application.Series;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>HTTP + handler + PostgreSQL desechable; solamente las fronteras externas son dobles.</summary>
public sealed class EmisionResultadoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public EmisionResultadoEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Theory]
    [InlineData("403", "Acceso denegado por el PAC")]
    [InlineData("400", "305: certificado no encontrado en lista LCO")]
    public async Task Emision_rechazada_devuelve_error_real_persiste_intento_y_replay_no_duplica(
        string codigo, string mensaje)
    {
        var pac = new PacRechaza(codigo, mensaje);
        await using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICfdiTimbradoPort>();
            services.AddSingleton<ICfdiTimbradoPort>(pac);
            services.RemoveAll<IPeriodoContablePort>();
            services.AddSingleton<IPeriodoContablePort, PeriodoAbierto>();
            services.RemoveAll<IEmpresaFiscalReadPort>();
            services.AddSingleton<IEmpresaFiscalReadPort, EmisorPrueba>();
            services.RemoveAll<IRequestHandler<ReservarFolioCommand, ReservarFolioResponse>>();
            services.AddTransient<IRequestHandler<ReservarFolioCommand, ReservarFolioResponse>, FolioPrueba>();
        }));
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin", Email = "dev-superadmin@dev.local",
            Nombre = "SuperAdmin", EmpresaId = (Guid?)null,
        });
        login.EnsureSuccessStatusCode();
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            loginJson.RootElement.GetProperty("accessToken").GetString());
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        // Sucursal y canal del seed; no se agregan filas a catálogos compartidos.
        var command = new EmitirFacturaVentaCommand(
            TestComprasFixtures.SucursalMid, "XAXX010101000", "Público en general", "616", "97000", "S01", "MEX",
            "AAA010101AAA", "601", "PUE", "01", "MXN", null, 1,
            ComportamientoFiscal.MostradorInmediato, null, null, false,
            [new(null, "01010101", "FIX vidrio de prueba", "H87", 1, 4000, 0, "02", 0.16m, null, null)]);
        var response = await client.PostAsJsonAsync("/api/v1/facturacion/facturas/", command);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var resultado = await response.Content.ReadFromJsonAsync<EmitirFacturaVentaResponse>();
        Assert.NotNull(resultado);
        Assert.Equal("TimbradoFallido", resultado.Estado);
        Assert.Null(resultado.Uuid);
        Assert.Equal(codigo, resultado.TimbradoErrorCodigo);
        Assert.Equal(mensaje, resultado.TimbradoErrorMensaje);
        Assert.EndsWith(resultado.Id.ToString(), response.Headers.Location!.ToString());

        var replay = await client.PostAsJsonAsync("/api/v1/facturacion/facturas/", command);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(resultado.Id, (await replay.Content.ReadFromJsonAsync<EmitirFacturaVentaResponse>())!.Id);
        Assert.Equal(1, pac.Intentos);

        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var factura = await db.FacturasVenta.AsNoTracking().SingleAsync(f => f.Id == resultado.Id);
        Assert.Null(factura.Uuid);
        Assert.Equal(codigo, factura.TimbradoErrorCodigo);
        Assert.Equal(mensaje, factura.TimbradoErrorMensaje);
        Assert.Equal(1, await db.BitacorasIntentoTimbrado.CountAsync(i => i.ComprobanteId == resultado.Id));
    }

    private sealed class PacRechaza(string codigo, string mensaje) : ICfdiTimbradoPort
    {
        public int Intentos { get; private set; }
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct)
        {
            Intentos++;
            return Task.FromResult(new TimbradoResultado(TimbradoEstado.Fallido,
                null, null, null, null, null, null, null, codigo, mensaje));
        }
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
            Task.FromResult<EmpresaFiscalLectura?>(new(empresaId, "AAA010101AAA", "FIX emisor", "601", 0.16m, "97000"));
        public Task<Guid?> ObtenerSucursalUnicaActivaAsync(CancellationToken ct) => Task.FromResult<Guid?>(TestComprasFixtures.SucursalMid);
    }

    private sealed class FolioPrueba : IRequestHandler<ReservarFolioCommand, ReservarFolioResponse>
    {
        public Task<ReservarFolioResponse> Handle(ReservarFolioCommand command, CancellationToken ct) =>
            Task.FromResult(new ReservarFolioResponse($"FIX-{Guid.NewGuid():N}", 1, ""));
    }
}
