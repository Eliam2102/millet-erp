using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Millet.Identidad.Application.Ports;

namespace Millet.Identidad.Infrastructure.Stubs;

/// <summary>
/// Correo de acceso simulado (plan 15, F4; ADR-0015). No envía nada: deja
/// en el log el destinatario y el UPN (nunca la contraseña) y guarda el
/// mensaje en memoria para que las pruebas lo inspeccionen.
///
/// Singleton: el buzón simulado se comparte entre requests y el worker.
/// </summary>
public sealed class CorreoSalienteSimulado : ICorreoSalientePort
{
    // PLATFORM-TODO(<CorreoSaliente>): reemplazar por el adaptador de Graph
    // (Mail.Send desde el buzón de servicio) cuando TI lo entregue (plan 15 §8).

    private readonly ConcurrentQueue<CorreoAccesoColaborador> _enviados = new();
    private readonly ILogger<CorreoSalienteSimulado> _logger;

    public CorreoSalienteSimulado(ILogger<CorreoSalienteSimulado> logger)
    {
        _logger = logger;
    }

    /// <summary>Correos "enviados" desde que arrancó el proceso.</summary>
    public IReadOnlyCollection<CorreoAccesoColaborador> Enviados => _enviados;

    public Task EnviarAccesoColaboradorAsync(CorreoAccesoColaborador correo, CancellationToken ct)
    {
        _enviados.Enqueue(correo);
        _logger.LogInformation(
            "Correo de acceso simulado para {Upn} enviado a {Destinatario}.",
            correo.Upn, correo.Destinatario);
        return Task.CompletedTask;
    }
}
