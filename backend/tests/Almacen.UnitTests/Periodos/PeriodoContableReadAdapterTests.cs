using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;

namespace Millet.Almacen.UnitTests.Periodos;

public sealed class PeriodoContableReadAdapterTests
{
    [Theory]
    [InlineData(EstadoPeriodo.Abierto, true, true)]
    [InlineData(EstadoPeriodo.Cerrado, true, false)]
    [InlineData(EstadoPeriodo.NoAbierto, true, false)]
    [InlineData(EstadoPeriodo.NoAbierto, false, false)]
    [InlineData(EstadoPeriodo.Abierto, false, false)]
    public async Task Solo_el_periodo_existente_y_abierto_admite_movimientos(
        EstadoPeriodo estado, bool existe, bool esperado)
    {
        var consulta = new ConsultaStub(new(2026, 9, estado, existe));
        var adapter = new PeriodoContableReadAdapter(consulta);
        using var cts = new CancellationTokenSource();

        (await adapter.EstaAbiertoAsync(2026, 9, cts.Token)).Should().Be(esperado);

        consulta.Fecha.Should().Be(new DateOnly(2026, 9, 1));
        consulta.Token.Should().Be(cts.Token);
    }

    [Fact]
    public async Task Registro_del_modulo_resuelve_el_adaptador_real_y_falla_cerrada()
    {
        var services = new ServiceCollection();
        services.AddAlmacenModule();
        services.AddScoped<IPeriodoContableConsultaPort>(_ => new ConsultaStub(new(2026, 9, EstadoPeriodo.Cerrado, true)));
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var port = scope.ServiceProvider.GetRequiredService<IPeriodoContableReadPort>();

        port.Should().BeOfType<PeriodoContableReadAdapter>();
        (await port.EstaAbiertoAsync(2026, 9, default)).Should().BeFalse();
    }

    private sealed class ConsultaStub(EstadoPeriodoContable estado) : IPeriodoContableConsultaPort
    {
        public DateOnly? Fecha { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<EstadoPeriodoContable> ConsultarPorFechaAsync(DateOnly fecha, CancellationToken ct)
        {
            Fecha = fecha;
            Token = ct;
            return Task.FromResult(estado);
        }

        // Resolver por fecha evita que diciembre termine en el periodo 13 de ajuste.
        public Task<EstadoPeriodoContable> ConsultarAsync(int anio, int numero, CancellationToken ct) =>
            throw new InvalidOperationException("Almacén debe consultar el mes ordinario de la fecha.");
    }
}
