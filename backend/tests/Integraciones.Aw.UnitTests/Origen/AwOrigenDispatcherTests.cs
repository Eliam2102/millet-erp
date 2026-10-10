using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Origen;
using Millet.Integraciones.Aw.Application.Workers;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Integraciones.Aw.UnitTests.Origen;

public sealed class AwOrigenDispatcherTests
{
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
    private sealed class Activo : IAwOrigenActivo
    {
        public string Origen { get; set; } = "Demo";
        public Task<AwOrigenEstado> LeerAsync(CancellationToken ct) => Task.FromResult(
            new AwOrigenEstado(Origen, true, true, "Simulado", "Simulado", null, null, 1));
    }
    private sealed class Clientes : IAwClientesOrigen
    {
        public int Lecturas { get; private set; }
        public Task<AwClienteOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct) =>
            Task.FromResult<AwClienteOrigenFila?>(null);
        public Task<AwClientesPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct)
        {
            Lecturas++;
            return Task.FromResult(new AwClientesPagina([], null));
        }
    }

    [Fact]
    public async Task Despacha_demo_y_relee_el_origen_al_siguiente_ciclo_sin_reiniciar()
    {
        var activo = new Activo();
        var demo = new Clientes();
        var real = new Clientes();
        var awDb = Guid.NewGuid().ToString();
        var maestroDb = Guid.NewGuid().ToString();
        var opciones = Options.Create(new AwClientesOptions { LecturaHabilitada = true, AplicacionHabilitada = true });
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<ICurrentEmpresaContext, Empresa>();
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddSingleton<IAwOrigenActivo>(activo);
        services.AddSingleton<IOptions<AwClientesOptions>>(opciones);
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<IntegracionesAwDbContext>(b => b.UseInMemoryDatabase(awDb));
        services.AddDbContext<CompartidoDbContext>(b => b.UseInMemoryDatabase(maestroDb));
        services.AddScoped<AplicarClienteAwService>();
        services.AddScoped<AwClientesSincronizador>();
        services.AddKeyedSingleton<IAwClientesOrigen>("Real", real);
        services.AddKeyedSingleton<IAwClientesOrigen>("Demo", demo);
        services.AddScoped<AwOrigenSesion>();
        services.AddScoped<AwOrigenSelectores>();
        services.AddScoped<IAwClientesOrigen>(sp => sp.GetRequiredService<AwOrigenSelectores>());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var demoEjec = AwClientesEjecucion.Crear("Demo", AwClientesEjecucionTipo.Barrido, "prueba", DateTimeOffset.UtcNow);
        var realEjec = AwClientesEjecucion.Crear("Simulado", AwClientesEjecucionTipo.Barrido, "prueba", DateTimeOffset.UtcNow);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>();
            db.ClientesEjecuciones.AddRange(demoEjec, realEjec);
            await db.SaveChangesAsync();
        }
        using var dispatcher = new AwClientesEjecucionDispatcher(provider.GetRequiredService<IServiceScopeFactory>(),
            opciones, NullLogger<AwClientesEjecucionDispatcher>.Instance);
        await dispatcher.DespacharAsync(default);
        demo.Lecturas.Should().Be(1);
        real.Lecturas.Should().Be(0);
        activo.Origen = "Real";
        await dispatcher.DespacharAsync(default);
        demo.Lecturas.Should().Be(1);
        real.Lecturas.Should().Be(1);
        using var final = provider.CreateScope();
        var ejecuciones = await final.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones.ToListAsync();
        ejecuciones.Should().OnlyContain(e => e.Estado == AwClientesEjecucionEstado.Completa);
    }
}
