using Microsoft.Extensions.DependencyInjection;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Origen;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.UnitTests.Origen;

public sealed class AwOrigenSelectorTests
{
    private sealed class Activo : IAwOrigenActivo
    {
        public string Origen { get; set; } = "Real";
        public bool Configurada { get; set; } = true;
        public Task<AwOrigenEstado> LeerAsync(CancellationToken ct) => Task.FromResult(
            new AwOrigenEstado(Origen, true, Configurada, "Sql", "Simulado", null, null, 1));
    }
    private sealed class Clientes(string cursor) : IAwClientesOrigen
    {
        public Task<AwClienteOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct) => Task.FromResult<AwClienteOrigenFila?>(null);
        public Task<AwClientesPagina> LeerPaginaAsync(string? desde, int tamano, CancellationToken ct) => Task.FromResult(new AwClientesPagina([], cursor));
    }
    private sealed class Pedidos(string numero) : IAwSolicitudesReader
    {
        public Task<IReadOnlyList<SolicitudAw>> LeerPendientesAsync(int max, CancellationToken ct) => Task.FromResult<IReadOnlyList<SolicitudAw>>([]);
        public Task<LecturaPedidoAw> LeerDatosPedidoAsync(string np, CancellationToken ct) => Task.FromResult(LecturaPedidoAw.DatosInvalidos(Millet.Facturacion.Domain.Ingesta.MotivoExcepcion.Otro, numero));
    }
    private static ServiceProvider Servicios(Activo activo) => new ServiceCollection()
        .AddKeyedSingleton<IAwClientesOrigen>("Real", new Clientes("sql"))
        .AddKeyedSingleton<IAwClientesOrigen>("Demo", new Clientes("pg"))
        .AddKeyedSingleton<IAwSolicitudesReader>("Real", new Pedidos("sql"))
        .AddKeyedSingleton<IAwSolicitudesReader>("Demo", new Pedidos("pg"))
        .AddSingleton<IAwOrigenActivo>(activo).AddScoped<AwOrigenSesion>().AddScoped<AwOrigenSelectores>()
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    [Fact]
    public async Task Cada_barrido_relee_el_origen_sin_reiniciar_y_conserva_la_pagina_en_curso()
    {
        var activo = new Activo();
        using var services = Servicios(activo);
        using var scope = services.CreateScope();
        var selector = scope.ServiceProvider.GetRequiredService<AwOrigenSelectores>();
        (await selector.LeerPaginaAsync(null, 10, default)).SiguienteCursor.Should().Be("sql");
        activo.Origen = "Demo";
        (await selector.LeerPaginaAsync("1", 10, default)).SiguienteCursor.Should().Be("sql");
        (await selector.LeerPaginaAsync(null, 10, default)).SiguienteCursor.Should().Be("pg");
        activo.Origen = "Real";
        (await selector.LeerPaginaAsync(null, 10, default)).SiguienteCursor.Should().Be("sql");
    }
    [Fact]
    public async Task Cada_ciclo_de_pedidos_relee_el_origen_y_no_cruza_bases_durante_el_ciclo()
    {
        var activo = new Activo();
        using var services = Servicios(activo);
        using var scope = services.CreateScope();
        var selector = scope.ServiceProvider.GetRequiredService<AwOrigenSelectores>();
        await selector.LeerPendientesAsync(10, default);
        activo.Origen = "Demo";
        (await selector.LeerDatosPedidoAsync("1", default)).Detalle.Should().Be("sql");
        await selector.LeerPendientesAsync(10, default);
        (await selector.LeerDatosPedidoAsync("1", default)).Detalle.Should().Be("pg");
    }
    [Fact]
    public async Task Demo_sin_cadena_no_cae_a_real_y_da_error_de_negocio()
    {
        using var services = Servicios(new Activo { Origen = "Demo", Configurada = false });
        using var scope = services.CreateScope();
        var selector = scope.ServiceProvider.GetRequiredService<AwOrigenSelectores>();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => selector.LeerPaginaAsync(null, 10, default));
        error.Message.Should().Be("La copia de demo de A+W no está configurada en este ambiente");
    }
}
