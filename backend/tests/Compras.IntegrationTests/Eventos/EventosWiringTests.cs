using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Application.Eventos;
using Millet.Compras.Domain.Events;

namespace Millet.Compras.IntegrationTests.Eventos;

/// <summary>
/// Smoke tests (F4-PR4) que verifican que los handlers in-proc de los
/// eventos de dominio están registrados en el contenedor DI. La
/// resolución vía <c>IEnumerable&lt;INotificationHandler&lt;T&gt;&gt;</c>
/// confirma que <c>AddMilletApplication</c> los descubrió por
/// assembly scan.
///
/// <para>
/// La verificación de que los handlers efectivamente loggean cuando
/// MediatR los invoca queda implícita en los tests de integración de
/// los flujos (Bifurcacion + Cancelar) — si esos pasan, los eventos se
/// publican y los handlers se ejecutan.
/// </para>
/// </summary>
public class EventosWiringTests : IClassFixture<StubsWebApplicationFactory>
{
    private readonly StubsWebApplicationFactory _factory;

    public EventosWiringTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void MatrizAprobacionSatisfechaHandler_EstaRegistrado()
    {
        using var scope = _factory.Services.CreateScope();
        var handlers = scope.ServiceProvider
            .GetServices<INotificationHandler<MatrizAprobacionSatisfechaEvent>>()
            .ToList();

        Assert.Contains(handlers, h => h is MatrizAprobacionSatisfechaLoggingHandler);
    }

    [Fact]
    public void CubrimientoRegistradoHandler_EstaRegistrado()
    {
        using var scope = _factory.Services.CreateScope();
        var handlers = scope.ServiceProvider
            .GetServices<INotificationHandler<CubrimientoRegistradoEvent>>()
            .ToList();

        Assert.Contains(handlers, h => h is CubrimientoRegistradoLoggingHandler);
    }

    [Fact]
    public void RequisicionCanceladaHandler_EstaRegistrado()
    {
        using var scope = _factory.Services.CreateScope();
        var handlers = scope.ServiceProvider
            .GetServices<INotificationHandler<RequisicionCanceladaEvent>>()
            .ToList();

        Assert.Contains(handlers, h => h is RequisicionCanceladaLoggingHandler);
    }
}
