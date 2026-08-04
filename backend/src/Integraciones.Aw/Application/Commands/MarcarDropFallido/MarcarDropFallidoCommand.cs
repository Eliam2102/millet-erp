using MediatR;

namespace Millet.Integraciones.Aw.Application.Commands.MarcarDropFallido;

/// <summary>
/// Comando del <c>AwDropWorker</c> cuando un drop falla. Si
/// <see cref="IsTerminal"/>=false solo incrementa el contador de
/// reintentos (Service Bus reintentará el mensaje). Si true, marca la
/// entidad como <c>FailedDrop</c> y emite
/// <c>AwEdiEntregaFallida</c> al Outbox para alertar.
/// </summary>
public sealed record MarcarDropFallidoCommand(
    Guid EntidadExternaId,
    string Error,
    string ErrorKind,
    bool IsTerminal) : IRequest;
