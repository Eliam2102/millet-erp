using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.CuentasPorPagar;

/// <summary>
/// Pruebas de integración HTTP del endpoint bajo demanda
/// POST /api/v1/cuentas-por-pagar/cfdis/simular-desde-oc/{ordenCompraId}.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class SimularCfdiHttpTests(WebApplicationFactory<Program> factory)
{
    private const string BaseUrl = "/api/v1/cuentas-por-pagar/cfdis";
    private static readonly Guid ProveedorId = Guid.Parse("00000005-0001-0000-0000-000000000001");
    private static readonly Guid ArticuloId = Guid.Parse("00000005-0002-0000-0000-000000000001");

    [Fact]
    public async Task SimularCfdiDesdeOc_Retorna201_Y_CfdiQuedaEnPorProcesar()
    {
        await using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
        using var admin = await LoginAsync(enEmpresa);

        var oc = CrearOcAutorizada(EmpresaBootstrapId, ProveedorId, ArticuloId);
        using (var scope = enEmpresa.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var comprasDb = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            comprasDb.OrdenesCompra.Add(oc);
            await comprasDb.SaveChangesAsync();
        }

        Guid cfdiId = Guid.Empty;
        try
        {
            var response = await admin.PostAsJsonAsync($"{BaseUrl}/simular-desde-oc/{oc.Id}", new { });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var body = await Json(response);

            // Verificar propiedades { cfdiRecibidoId, uuid/uuidCfdi, folio, total }
            var cfdiRecibidoIdProp = body.TryGetProperty("cfdiRecibidoId", out var idProp)
                ? idProp.GetGuid()
                : body.GetProperty("id").GetGuid();
            Assert.NotEqual(Guid.Empty, cfdiRecibidoIdProp);
            cfdiId = cfdiRecibidoIdProp;

            var uuidProp = body.TryGetProperty("uuidCfdi", out var uCfdi)
                ? uCfdi.GetString()
                : body.GetProperty("uuid").GetString();
            Assert.False(string.IsNullOrWhiteSpace(uuidProp));

            Assert.Equal(oc.Folio.Valor, body.GetProperty("folio").GetString());
            Assert.True(body.GetProperty("total").GetDecimal() > 0m);

            // Verificar GET /api/v1/cuentas-por-pagar/cfdis/{cfdiRecibidoId} en estado PorProcesar
            var getResp = await admin.GetAsync($"{BaseUrl}/{cfdiId}");
            Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

            var detalle = await Json(getResp);
            var estadoProp = detalle.GetProperty("estado");
            if (estadoProp.ValueKind == JsonValueKind.Number)
            {
                Assert.Equal((int)EstadoCfdiRecibido.PorProcesar, estadoProp.GetInt32());
            }
            else
            {
                Assert.Equal(nameof(EstadoCfdiRecibido.PorProcesar), estadoProp.GetString());
            }
        }
        finally
        {
            using var scope = enEmpresa.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var cxpDb = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var comprasDb = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();

            if (cfdiId != Guid.Empty)
            {
                await cxpDb.CfdisRecibidos.Where(c => c.Id == cfdiId).ExecuteDeleteAsync();
            }

            await comprasDb.AutorizacionesOc.Where(a => a.OrdenCompraId == oc.Id).ExecuteDeleteAsync();
            await comprasDb.LineasOrdenCompra.Where(l => l.OrdenCompraId == oc.Id).ExecuteDeleteAsync();
            await comprasDb.OrdenesCompra.Where(o => o.Id == oc.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task SimularCfdiDesdeOc_EnProduccion_Retorna404NotFound()
    {
        var authPrevio = Environment.GetEnvironmentVariable("Auth__Mode");
        Environment.SetEnvironmentVariable("Auth__Mode", "EntraId");

        var oc = CrearOcAutorizada(EmpresaBootstrapId, ProveedorId, ArticuloId);

        try
        {
            await using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
            await using var prodApp = enEmpresa.WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Production");
                b.UseSetting("Auth:Jwt:SigningKey", "dev-only-local-signing-key-NOT-FOR-PROD-32-bytes-mininum-required-for-hmac-sha256");
                b.ConfigureTestServices(services =>
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "ProdTestScheme";
                        options.DefaultChallengeScheme = "ProdTestScheme";
                        options.DefaultForbidScheme = "ProdTestScheme";
                    }).AddScheme<AuthenticationSchemeOptions, ProdTestAuthHandler>("ProdTestScheme", _ => { });
                });
            });

            using (var scope = enEmpresa.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var comprasDb = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
                comprasDb.OrdenesCompra.Add(oc);
                await comprasDb.SaveChangesAsync();
            }

            using var client = prodApp.CreateClientWithIdempotency();
            var response = await client.PostAsJsonAsync($"{BaseUrl}/simular-desde-oc/{oc.Id}", new { });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__Mode", authPrevio);

            using var scope = factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var comprasDb = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();

            await comprasDb.AutorizacionesOc.Where(a => a.OrdenCompraId == oc.Id).ExecuteDeleteAsync();
            await comprasDb.LineasOrdenCompra.Where(l => l.OrdenCompraId == oc.Id).ExecuteDeleteAsync();
            await comprasDb.OrdenesCompra.Where(o => o.Id == oc.Id).ExecuteDeleteAsync();
        }
    }

    private static OrdenCompra CrearOcAutorizada(Guid empresaId, Guid proveedorId, Guid articuloId)
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: Millet.Compras.Domain.Oc.Folio.Parse($"OC-MID2026-{Random.Shared.Next(100000, 999999):D6}"),
            folioAnio: 2026,
            proveedorId: proveedorId,
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Prueba CFDI simulación HTTP");

        var lineaId = Guid.CreateVersion7();
        oc.AgregarLineaManual(
            lineaId: lineaId,
            articuloId: articuloId,
            cantidad: 10m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7());

        var ahora = DateTimeOffset.UtcNow;
        oc.ActualizarDatosFactura("G03", "PPD", "99");
        oc.EnviarAAutorizacion(ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), ahora);
        if (oc.RequiereNivel2)
        {
            oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel2, Guid.CreateVersion7(), ahora);
        }

        return oc;
    }

    private sealed class ProdTestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim>
            {
                new(MilletClaimTypes.Sub, Guid.NewGuid().ToString()),
                new(MilletClaimTypes.Name, "Prod Test User"),
                new(MilletClaimTypes.CurrentEmpresaId, EmpresaBootstrapId.ToString()),
                new(MilletClaimTypes.AuthType, MilletClaimTypes.AuthTypes.ServicePrincipal),
                new(MilletClaimTypes.Permission, PermisosCanonicos.CuentasPorPagarCfdisLeer),
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
