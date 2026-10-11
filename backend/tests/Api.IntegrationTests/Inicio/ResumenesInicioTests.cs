using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Administracion.Application.Abstractions;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Inicio;

namespace Millet.Api.IntegrationTests.Inicio;

/// <summary>Host PostgreSQL desechable de la suite. No usa /fake-login ni cambia el modo de acceso de la app.</summary>
public sealed class ResumenesInicioTests(ResumenesInicioFactory factory) : IClassFixture<ResumenesInicioFactory>
{
    public static TheoryData<string, string> Endpoints => new()
    {
        { "compras/inicio/requisiciones", PermisosCanonicos.ComprasRequisicionesLeer },
        { "compras/inicio/autorizar-rq", PermisosCanonicos.ComprasRequisicionesLeer },
        { "compras/inicio/autorizar-oc", PermisosCanonicos.ComprasOrdenesLeer },
        { "compras/inicio/recepciones", PermisosCanonicos.ComprasOrdenesLeer },
        { "compras/inicio/circuito", PermisosCanonicos.ComprasOrdenesLeer },
        { "almacen/inicio/recepciones", PermisosCanonicos.AlmacenEntradasLeer },
        { "almacen/inicio/conteos", PermisosCanonicos.AlmacenInventariosLeer },
        { "almacen/inicio/vales", PermisosCanonicos.AlmacenSalidasLeerTodas },
        { "cuentas-por-pagar/inicio/facturas", PermisosCanonicos.CuentasPorPagarFacturasLeer },
        { "tesoreria/inicio/pagos", PermisosCanonicos.TesoreriaPasivosVer },
        { "tesoreria/inicio/bancos", PermisosCanonicos.TesoreriaCuentasVer },
        { "tesoreria/inicio/depositos", PermisosCanonicos.TesoreriaDepositosConfirmar },
        { "tesoreria/inicio/flujo", PermisosCanonicos.TesoreriaMovimientosVer },
        { "cuentas-por-cobrar/inicio/vencido", PermisosCanonicos.CuentasPorCobrarCarteraLeer },
        { "cuentas-por-cobrar/inicio/circuito", PermisosCanonicos.CuentasPorCobrarCarteraLeer },
        { "facturacion/inicio/pedidos", PermisosCanonicos.FacturacionFacturasLeer },
        { "facturacion/inicio/timbrado", PermisosCanonicos.FacturacionFacturasLeer },
        { "facturacion/inicio/circuito", PermisosCanonicos.FacturacionFacturasLeer },
        { "contabilidad/inicio/periodo", PermisosCanonicos.ContabilidadPeriodoLeer },
    };
    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Cada_resumen_sin_permiso_es_403(string ruta, string permiso)
    {
        using var client = Cliente();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/" + ruta)).StatusCode);
        // También con permisos de otro recurso.
        using var otro = Cliente(permiso == PermisosCanonicos.TesoreriaCuentasVer ? PermisosCanonicos.ComprasOrdenesLeer : PermisosCanonicos.TesoreriaCuentasVer);
        Assert.Equal(HttpStatusCode.Forbidden, (await otro.GetAsync("/api/v1/" + ruta)).StatusCode);
    }
    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Cada_resumen_scoped_rechaza_seleccion_ajena_antes_de_leer(string ruta, string permiso)
    {
        if (ruta.StartsWith("contabilidad/", StringComparison.Ordinal)) return; // Periodo global de la empresa.
        using var client = Cliente(permiso, PermisosCanonicos.ComprasRequisicionesAutorizarNivel1, PermisosCanonicos.ComprasOrdenesAutorizarNivel1);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/v1/{ruta}?sucursalId={ResumenesInicioFactory.Ajena}")).StatusCode);
    }
    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Cada_resumen_con_permiso_respeta_alcance_operativo_y_corporativo(string ruta, string permiso)
    {
        var lecturas = new[] { permiso, PermisosCanonicos.ComprasRequisicionesAutorizarNivel1,
            PermisosCanonicos.ComprasOrdenesAutorizarNivel1, PermisosCanonicos.FacturacionCajaLeerTodas };
        using var operativo = Cliente(lecturas);
        using var corporativo = Cliente([.. lecturas, PermisosCanonicos.ComprasRequisicionesLeerTodasSucursales,
            "compras.ordenes.leer-todas-sucursales", "almacen.documentos.leer-todas-sucursales",
            "cuentas_por_pagar.documentos.leer-todas-sucursales", "cuentas_por_cobrar.cartera.leer-todas-sucursales",
            "tesoreria.documentos.leer-todas-sucursales", "facturacion.documentos.leer-todas-sucursales"]);
        var propio = await operativo.GetFromJsonAsync<ResumenInicioResponse>("/api/v1/" + ruta);
        var seleccionado = await corporativo.GetFromJsonAsync<ResumenInicioResponse>($"/api/v1/{ruta}?sucursalId={ResumenesInicioFactory.Propia}");
        var todas = await corporativo.GetFromJsonAsync<ResumenInicioResponse>("/api/v1/" + ruta);
        Assert.NotNull(propio); Assert.NotNull(seleccionado); Assert.NotNull(todas);
        Assert.True(todas.Total >= propio.Total);
        if (ruta is "compras/inicio/circuito" or "compras/inicio/recepciones")
            Assert.True(seleccionado.Total >= propio.Total); // Corporativo ve los compradores de la sucursal seleccionada.
        else
        {
            Assert.Equal(propio.Total, seleccionado.Total);
            Assert.Equal(propio.Valor, seleccionado.Valor);
            Assert.Equal(propio.Etapas, seleccionado.Etapas);
            Assert.Equal(propio.Serie, seleccionado.Serie);
        }
    }
    [Theory]
    [InlineData("compras/inicio/autorizar-rq", PermisosCanonicos.ComprasRequisicionesLeer)]
    [InlineData("compras/inicio/autorizar-oc", PermisosCanonicos.ComprasOrdenesLeer)]
    public async Task Lectura_sin_firma_no_permite_resumen_de_autorizacion(string ruta, string permiso)
    {
        using var client = Cliente(permiso);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/" + ruta)).StatusCode);
    }
    [Fact]
    public async Task PostgreSQL_operativo_cuenta_una_sucursal_corporativo_ambas_y_topbar_restringe()
    {
        var usuario = ResumenesInicioFactory.Usuario;
        var empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
        var ids = new List<Guid>();
        using var operativo = Cliente(PermisosCanonicos.ComprasRequisicionesLeer);
        using var corporativo = Cliente(PermisosCanonicos.ComprasRequisicionesLeer, PermisosCanonicos.ComprasRequisicionesLeerTodasSucursales);
        const string ruta = "/api/v1/compras/inicio/requisiciones";
        var antesOperativo = (await operativo.GetFromJsonAsync<ResumenInicioResponse>(ruta))!.Total;
        var antesCorporativo = (await corporativo.GetFromJsonAsync<ResumenInicioResponse>(ruta))!.Total;
        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
                foreach (var sucursal in new[] { ResumenesInicioFactory.Propia, ResumenesInicioFactory.Ajena })
                {
                    var id = Guid.CreateVersion7(); ids.Add(id);
                    db.Requisiciones.Add(new Requisicion(id, empresa, Folio.Parse($"INI2026-{Random.Shared.Next(100000,999999)}"), 2026,
                        Clasificacion.OrdenCompra, sucursal, Guid.Parse("00000005-0004-0000-0000-000000000001"),
                        null, usuario, usuario, Prioridad.Normal, DateTimeOffset.UtcNow));
                }
                await db.SaveChangesAsync();
            }
            Assert.Equal(antesOperativo + 1, (await operativo.GetFromJsonAsync<ResumenInicioResponse>(ruta))!.Total);
            Assert.Equal(antesCorporativo + 2, (await corporativo.GetFromJsonAsync<ResumenInicioResponse>(ruta))!.Total);
            var seleccionado = await corporativo.GetFromJsonAsync<ResumenInicioResponse>($"{ruta}?sucursalId={ResumenesInicioFactory.Propia}");
            Assert.Equal(antesOperativo + 1, seleccionado!.Total);
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().Requisiciones.Where(r => ids.Contains(r.Id)).ExecuteDeleteAsync();
        }
    }
    private HttpClient Cliente(params string[] permisos)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-IniRol-Permisos", string.Join('|', permisos));
        return client;
    }
}

public sealed class ResumenesInicioFactory : WebApplicationFactory<Program>
{
    internal static readonly Guid Propia = Guid.Parse("00000005-0003-0000-0000-000000000001");
    internal static readonly Guid Ajena = Guid.Parse("00000005-0003-0000-0000-000000000002");
    internal static readonly Guid Usuario = Guid.Parse("00000006-0000-0000-0000-000000000001");
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureTestServices(services =>
    {
        services.AddAuthentication(options => {
            options.DefaultAuthenticateScheme = "IniRolTests"; options.DefaultChallengeScheme = "IniRolTests";
            options.DefaultForbidScheme = "IniRolTests";
        }).AddScheme<AuthenticationSchemeOptions, AutenticacionPrueba>("IniRolTests", _ => { });
        services.RemoveAll<IUsuarioSucursalReadPort>();
        services.AddSingleton<IUsuarioSucursalReadPort, SucursalesPrueba>();
    });
    private sealed class SucursalesPrueba : IUsuarioSucursalReadPort
    {
        public Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid usuarioId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([Propia]);
        public Task<bool> EstaAsociadoAsync(Guid usuarioId, Guid sucursalId, CancellationToken cancellationToken) => Task.FromResult(sucursalId == Propia);
    }
    private sealed class AutenticacionPrueba(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> {
                new(MilletClaimTypes.Sub, Usuario.ToString()), new(MilletClaimTypes.Name, "INI-ROL Prueba"),
                new(MilletClaimTypes.CurrentEmpresaId, "00000003-0000-0000-0000-000000000001"),
                new(MilletClaimTypes.AuthType, MilletClaimTypes.AuthTypes.ServicePrincipal),
            };
            claims.AddRange(Request.Headers["X-IniRol-Permisos"].ToString().Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => new Claim(MilletClaimTypes.Permission, p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
}
