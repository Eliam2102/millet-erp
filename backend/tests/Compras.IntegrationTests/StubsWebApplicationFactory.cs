using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests;

/// <summary>
/// Factory que sube el host del Api con <c>Compras:UseStubs=true</c>.
///
/// <para>
/// La config se aplica vía <b>variables de entorno</b> (en el static
/// constructor) y NO vía <c>ConfigureAppConfiguration</c>. Razón:
/// <c>Program.cs</c> llama <c>builder.Services.AddComprasStubs(...)</c>
/// que lee la config <i>eagerly</i> durante la ejecución de Program.
/// Los overrides de <c>WebApplicationFactory.ConfigureWebHost</c>
/// llegan demasiado tarde — se aplican durante <c>builder.Build()</c>,
/// después de que <c>AddComprasStubs</c> ya decidió no registrar nada.
/// </para>
/// <para>
/// Las env vars sí están disponibles desde el inicio de
/// <c>WebApplication.CreateBuilder(args)</c>, así que llegan a tiempo.
/// La doble-underscore (<c>__</c>) es la convención .NET para
/// representar el separador de sección (<c>:</c>) en env vars.
/// </para>
/// <para>
/// El environment se fuerza a <c>Development</c> para:
/// </para>
/// <list type="bullet">
///   <item>cargar <c>appsettings.Development.json</c> del Api (fake auth + connection string).</item>
///   <item>NO disparar el guardia "stubs en Production" del bootstrap.</item>
/// </list>
/// </summary>
public sealed class StubsWebApplicationFactory : WebApplicationFactory<Program>
{
    static StubsWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("Compras__UseStubs", "true");
        Environment.SetEnvironmentVariable("Compras__Stubs__Stock__DefaultRatio", "1.0");
        Environment.SetEnvironmentVariable(
            "Compras__Stubs__Stock__Ratios__00000000-0000-0000-0000-000000000aaa", "0.5");
        Environment.SetEnvironmentVariable(
            "Compras__Stubs__Stock__Ratios__00000000-0000-0000-0000-000000000bbb", "0.0");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }

    /// <summary>
    /// Tras construir el host (migraciones aplicadas + bootstrap del
    /// SuperAdmin terminado), forzamos
    /// <c>ComprasSettings.AutoGenerarOcAlAutorizar=true</c> para todas
    /// las empresas existentes. Esto preserva el comportamiento que
    /// asume <see cref="Bifurcacion.BifurcacionEndpointsTests"/>,
    /// <see cref="Bifurcacion.CancelarEndpointsTests"/>,
    /// <see cref="Stubs.StubsTests"/> y
    /// <see cref="Outbox.OutboxIntegrationTests"/>: que el handler de
    /// Autorizar invoque el puerto OC y se cree la fila
    /// <c>oc_borrador_stub</c>. El default productivo (false) lo cubren
    /// tests dedicados en <see cref="Settings.ComprasSettingsEndpointsTests"/>.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        SeedAutoGenerarOcForAllEmpresas(host);
        return host;
    }

    private static void SeedAutoGenerarOcForAllEmpresas(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        var settings = db.ComprasSettings.ToList();
        foreach (var s in settings)
        {
            s.EstablecerAutoGenerarOcAlAutorizar(true);
        }
        db.SaveChanges();
    }
}
