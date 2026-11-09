using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Oc.Events;
using Millet.CuentasPorPagar.Application.Cfdi.SimularCfdi;
using Millet.CuentasPorPagar.Application.EventListeners;

namespace Millet.CuentasPorPagar.UnitTests.EventListeners;

public sealed class OrdenCompraEnviadaAlProveedorListenerTests
{
    [Fact]
    public async Task Handle_NotificacionValida_DelegaASenderConComandoCorrecto()
    {
        var sender = new FakeSender();
        var logger = new CapturingLogger<OrdenCompraEnviadaAlProveedorListener>();
        var listener = new OrdenCompraEnviadaAlProveedorListener(sender, logger);

        var ocId = Guid.NewGuid();
        var empresaId = Guid.NewGuid();
        var folio = "OC-MID2026-000123";
        var notification = new OrdenCompraEnviadaAlProveedorNotification(ocId, empresaId, folio);

        await listener.Handle(notification, CancellationToken.None);

        sender.ComandosEnviados.Should().HaveCount(1);
        var comando = sender.ComandosEnviados[0].Should().BeOfType<GenerarEIngresarCfdiPruebaCommand>().Subject;
        comando.OrdenCompraId.Should().Be(ocId);
        comando.CantidadesPorPosicion.Should().BeNull();

        logger.Logs.Should().Contain(l =>
            l.LogLevel == LogLevel.Information &&
            l.Message.Contains(ocId.ToString()) &&
            l.Message.Contains(folio));
    }

    [Fact]
    public async Task Handle_CuandoSenderFalla_CapturaExcepcionSinRelanzar_YRegistraErrorEnLog()
    {
        var sender = new FakeSender { DebeFallar = true };
        var logger = new CapturingLogger<OrdenCompraEnviadaAlProveedorListener>();
        var listener = new OrdenCompraEnviadaAlProveedorListener(sender, logger);

        var ocId = Guid.NewGuid();
        var notification = new OrdenCompraEnviadaAlProveedorNotification(ocId, Guid.NewGuid(), "OC-MID2026-000999");

        var ex = await Record.ExceptionAsync(() => listener.Handle(notification, CancellationToken.None));

        ex.Should().BeNull();
        sender.ComandosEnviados.Should().HaveCount(1);

        logger.Logs.Should().Contain(l =>
            l.LogLevel == LogLevel.Error &&
            l.Message.Contains(ocId.ToString()) &&
            l.Exception is InvalidOperationException);
    }

    private sealed class FakeSender : ISender
    {
        public bool DebeFallar { get; set; }
        public List<object> ComandosEnviados { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            ComandosEnviados.Add(request);
            if (DebeFallar)
            {
                throw new InvalidOperationException("Fallo de prueba en ISender");
            }

            if (typeof(TResponse) == typeof(GenerarEIngresarCfdiPruebaResponse))
            {
                var response = new GenerarEIngresarCfdiPruebaResponse(Guid.NewGuid(), Guid.NewGuid().ToString(), "F-1", 100m);
                return Task.FromResult((TResponse)(object)response);
            }

            return Task.FromResult(default(TResponse)!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
        {
            ComandosEnviados.Add(request);
            if (DebeFallar)
            {
                throw new InvalidOperationException("Fallo de prueba en ISender");
            }
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            ComandosEnviados.Add(request);
            if (DebeFallar)
            {
                throw new InvalidOperationException("Fallo de prueba en ISender");
            }
            return Task.FromResult<object?>(null);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel LogLevel, string Message, Exception? Exception)> Logs { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Logs.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
