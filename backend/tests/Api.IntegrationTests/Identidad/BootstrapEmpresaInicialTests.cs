using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Identidad;

/// <summary>
/// Empresa inicial del bootstrap frente a la empresa provisional que la
/// migración F1-ADM-01 inserta en una base limpia (RFC
/// <see cref="Empresa.RfcProvisional"/>). Incidente QA 20-oct-2026: el
/// arranque encontraba la provisional por id y sólo creaba la asignación,
/// sin aplicar <c>Auth:Bootstrap:EmpresaInicial</c>.
/// </summary>
public class BootstrapEmpresaInicialTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public BootstrapEmpresaInicialTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task EmpresaProvisional_ConConfiguracion_RecibeDatosReales_Y_Asignacion()
    {
        var configurada = OpcionesDelHost().Bootstrap!.EmpresaInicial!;

        // El host de pruebas arranca sobre base limpia: la provisional ya debe estar configurada.
        var alArrancar = await LeerEmpresaAsync();
        Assert.Equal(configurada.Rfc, alArrancar.Rfc);

        await RegresarAProvisionalAsync();
        var provisional = await LeerEmpresaAsync();
        Assert.True(provisional.EsProvisional);

        await BootstrapDelHost().StartAsync(CancellationToken.None);

        var empresa = await LeerEmpresaAsync();
        Assert.Equal(configurada.Rfc, empresa.Rfc);
        Assert.Equal(configurada.Rfc.ToUpperInvariant(), empresa.Clave);
        Assert.Equal(configurada.RazonSocial, empresa.RazonSocial);
        Assert.Equal(configurada.RegimenFiscal, empresa.RegimenFiscal);
        Assert.Equal(configurada.NombreComercial, empresa.NombreComercial);
        Assert.Equal(provisional.Version + 1, empresa.Version);
        Assert.Equal(1, await ContarAsignacionesSuperAdminAsync());
    }

    [Fact]
    public async Task EmpresaConRfcReal_NoCambia_AunqueLaConfiguracionSeaOtra()
    {
        var antes = await LeerEmpresaAsync();
        Assert.False(antes.EsProvisional);

        var otra = new BootstrapSuperAdminOptions
        {
            InitialAdminEntraOid = SuperAdminOid,
            Bootstrap = new BootstrapEmpresaInicial
            {
                EmpresaInicial = new EmpresaInicialData
                {
                    Rfc = "MIN120224147",
                    RazonSocial = "M INDUSTRIA SA DE C V",
                    RegimenFiscal = "601",
                    NombreComercial = "Millet Industria de Vidrio",
                },
            },
        };
        var bootstrap = new BootstrapSuperAdminHostedService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(otra),
            _factory.Services.GetRequiredService<ILogger<BootstrapSuperAdminHostedService>>());
        await bootstrap.StartAsync(CancellationToken.None);

        var despues = await LeerEmpresaAsync();
        Assert.Equal(antes.Rfc, despues.Rfc);
        Assert.Equal(antes.Clave, despues.Clave);
        Assert.Equal(antes.RazonSocial, despues.RazonSocial);
        Assert.Equal(antes.NombreComercial, despues.NombreComercial);
        Assert.Equal(antes.Version, despues.Version);
    }

    [Fact]
    public async Task ReArranque_NoCambia_Empresa_Ni_Duplica_Asignacion()
    {
        var antes = await LeerEmpresaAsync();

        await BootstrapDelHost().StartAsync(CancellationToken.None);
        await BootstrapDelHost().StartAsync(CancellationToken.None);

        var despues = await LeerEmpresaAsync();
        Assert.Equal(antes.Rfc, despues.Rfc);
        Assert.Equal(antes.Clave, despues.Clave);
        Assert.Equal(antes.RazonSocial, despues.RazonSocial);
        Assert.Equal(antes.Version, despues.Version);
        Assert.Equal(1, await ContarAsignacionesSuperAdminAsync());
    }

    private BootstrapSuperAdminOptions OpcionesDelHost() =>
        _factory.Services.GetRequiredService<IOptions<BootstrapSuperAdminOptions>>().Value;

    private BootstrapSuperAdminHostedService BootstrapDelHost() =>
        _factory.Services.GetServices<IHostedService>()
            .OfType<BootstrapSuperAdminHostedService>().Single();

    private async Task<Empresa> LeerEmpresaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await db.Empresas.AsNoTracking().SingleAsync(e => e.Id == EmpresaInicialId);
    }

    /// <summary>Deja la fila como la inserta la migración en una base limpia.</summary>
    private async Task RegresarAProvisionalAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE compartido.empresas
            SET rfc = {0}, clave = {0}, razon_social = 'Empresa inicial pendiente de configuración',
                regimen_fiscal = '601', nombre_comercial = NULL
            WHERE id = {1}
            """,
            Empresa.RfcProvisional, EmpresaInicialId);
    }

    private async Task<int> ContarAsignacionesSuperAdminAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var usuarioId = await db.Usuarios.Where(u => u.EntraOid == SuperAdminOid).Select(u => u.Id).SingleAsync();
        var rolId = await db.Roles.Where(r => r.Codigo == "super-admin").Select(r => r.Id).SingleAsync();
        return await db.UsuarioEmpresaRoles.CountAsync(
            a => a.UsuarioId == usuarioId && a.EmpresaId == EmpresaInicialId && a.RolId == rolId);
    }
}
