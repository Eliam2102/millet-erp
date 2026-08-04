namespace Millet.Integraciones.Aw.Application.Workers;

/// <summary>
/// Abstracción de los 3 callbacks de Service Bus (Complete / Abandon /
/// DeadLetter) que <see cref="AwDropWorker"/> invoca tras procesar un
/// mensaje. Permite que la lógica del worker (qué decisión tomar para
/// cada caso) se pruebe unit sin levantar Service Bus real ni mockear
/// el complejo <c>ProcessMessageEventArgs</c>.
///
/// <para>
/// Implementación productiva: <c>ServiceBusMessageCompletion</c>
/// (private nested class del worker) que delega a
/// <c>ProcessMessageEventArgs</c>. Implementación de tests: un fake que
/// captura cuál método se llamó (Complete vs Abandon vs DeadLetter) y
/// con qué razón.
/// </para>
/// </summary>
public interface IMessageCompletion
{
    Task CompleteAsync(CancellationToken cancellationToken);
    Task AbandonAsync(CancellationToken cancellationToken);
    Task DeadLetterAsync(string reason, string description, CancellationToken cancellationToken);
}
