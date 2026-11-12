using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Infrastructure.Workers;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Api.UnitTests.ComercioExterior;

public sealed class RecepcionCeWorkerTests
{
    [Theory]
    [InlineData("almacen.oc_recepcion.registrada.v1")]
    [InlineData("almacen.recepcion_oc.cancelada.v1")]
    [InlineData("almacen.recepcion_activo.anulada.v1")]
    [InlineData("almacen.oc_devolucion.registrada.v1")]
    public async Task Cambios_de_recepcion_llegan_a_la_conciliacion(string tipo)
    {
        var empresa = Guid.NewGuid(); var oc = Guid.NewGuid(); var linea = Guid.NewGuid(); var evento = Guid.NewGuid();
        var captura = new Captura();
        var services = new ServiceCollection();
        services.AddMediatR(c => c.RegisterServicesFromAssemblyContaining<RecibirRecepcionCeCommand>());
        services.AddSingleton<IRequestHandler<RecibirRecepcionCeCommand>>(captura);
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddSingleton<ICurrentEmpresaContext>(new CurrentEmpresaContext(new HttpContextAccessor()));
        using var sp = services.BuildServiceProvider();
        using var worker = new AlmacenEventListenerWorker(null!, sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<AlmacenEventListenerWorker>.Instance);
        var body = JsonSerializer.Serialize(new {
            EmpresaId = empresa, OrdenCompraId = oc, OrdenCompraOrigenId = oc, LineaOcId = linea,
            FechaMovimiento = new DateOnly(2026, 10, 15), OcurridoEn = DateTimeOffset.Parse("2026-10-15T18:00:00Z"),
            CantidadRecibidaAcumulada = 1m,
            Lineas = new[] { new { LineaOcId = linea, CantidadRecibidaAcumulada = 1m, RecibidoAcumuladoDocumento = 1m } }
        });
        await ((ILocalIntegrationEventConsumer)worker).ConsumirLocalAsync(tipo, evento, body, default);
        var command = Assert.Single(captura.Comandos);
        Assert.Equal(evento, command.EventoId); Assert.Equal(empresa, command.Datos.EmpresaId);
        Assert.Equal(oc, command.Datos.OrdenCompraId); Assert.Equal(linea, Assert.Single(command.Datos.Lineas).LineaOcId);
    }

    private sealed class Captura : IRequestHandler<RecibirRecepcionCeCommand>
    {
        public List<RecibirRecepcionCeCommand> Comandos { get; } = [];
        public Task Handle(RecibirRecepcionCeCommand request, CancellationToken cancellationToken)
        { Comandos.Add(request); return Task.CompletedTask; }
    }
}
