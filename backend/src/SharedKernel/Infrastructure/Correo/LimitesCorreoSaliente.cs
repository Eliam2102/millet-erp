using Millet.SharedKernel.Application.Correo;

namespace Millet.SharedKernel.Infrastructure.Correo;

/// <summary>
/// Control de volumen del buzón del ERP (86e3zr74h): espera turno al llegar a
/// <see cref="LimitesCorreoOptions.MensajesPorMinuto"/>, rechaza mensajes con
/// más destinatarios de los permitidos, corta al tope diario con un error
/// reintentable y reintenta con espera creciente cuando el proveedor limita
/// (<see cref="CodigosCorreoSaliente.LimiteProveedor"/>). Si los reintentos se
/// agotan devuelve el error: el módulo dueño conserva el envío y lo reintenta.
/// </summary>
public sealed class LimitesCorreoSaliente(
    ICorreoSaliente interno,
    LimitesCorreoOptions opciones,
    TimeProvider tiempo,
    Func<TimeSpan, CancellationToken, Task>? esperar = null) : ICorreoSaliente, IDisposable
{
    private static readonly TimeSpan Minuto = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Dia = TimeSpan.FromHours(24);

    // ponytail: contadores en memoria por instancia; se reinician al reiniciar la API.
    // Si la API escala a varias instancias, contar desde core.correo_envio.
    private readonly SemaphoreSlim _turno = new(1, 1);
    private readonly Queue<DateTimeOffset> _ultimoMinuto = new();
    private readonly Queue<(DateTimeOffset Cuando, int Destinatarios)> _ultimoDia = new();
    private int _destinatariosDia;
    private readonly Func<TimeSpan, CancellationToken, Task> _esperar =
        esperar ?? ((t, ct) => Task.Delay(t, tiempo, ct));

    public bool EsSimulado => interno.EsSimulado;

    public void Dispose() => _turno.Dispose();

    public async Task<ResultadoCorreoSaliente> EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
    {
        var destinatarios = mensaje.Para.Count;
        if (destinatarios > opciones.MaxDestinatariosPorMensaje)
            return ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.ExcedeDestinatarios,
                $"El correo trae {destinatarios} destinatarios; el máximo por mensaje es {opciones.MaxDestinatariosPorMensaje}.");

        for (var intento = 0; ; intento++)
        {
            var bloqueo = await TomarTurnoAsync(destinatarios, cancellationToken);
            if (bloqueo is not null) return bloqueo;

            var resultado = await interno.EnviarAsync(mensaje, cancellationToken);
            if (resultado.Codigo != CodigosCorreoSaliente.LimiteProveedor || intento >= opciones.Reintentos)
                return resultado;

            await _esperar(TimeSpan.FromSeconds(opciones.EsperaInicialSegundos * Math.Pow(2, intento)), cancellationToken);
        }
    }

    /// <summary>Espera turno dentro del límite por minuto y aparta los destinatarios del día. Devuelve el error si el día está lleno.</summary>
    private async Task<ResultadoCorreoSaliente?> TomarTurnoAsync(int destinatarios, CancellationToken cancellationToken)
    {
        await _turno.WaitAsync(cancellationToken);
        try
        {
            var ahora = tiempo.GetUtcNow();
            while (_ultimoDia.TryPeek(out var d) && ahora - d.Cuando >= Dia)
                _destinatariosDia -= _ultimoDia.Dequeue().Destinatarios;
            if (_destinatariosDia + destinatarios > opciones.MaxDestinatariosPorDia)
            {
                var libre = _ultimoDia.TryPeek(out var primero) ? primero.Cuando + Dia : ahora;
                return ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.LimiteDiario,
                    $"Se llegó al tope de {opciones.MaxDestinatariosPorDia} destinatarios en 24 h; se libera cupo a partir de {libre:yyyy-MM-dd HH:mm} UTC.");
            }

            while (true)
            {
                ahora = tiempo.GetUtcNow();
                while (_ultimoMinuto.TryPeek(out var m) && ahora - m >= Minuto) _ultimoMinuto.Dequeue();
                if (_ultimoMinuto.Count < opciones.MensajesPorMinuto) break;
                await _esperar(_ultimoMinuto.Peek() + Minuto - ahora, cancellationToken);
            }

            _ultimoMinuto.Enqueue(ahora);
            _ultimoDia.Enqueue((ahora, destinatarios));
            _destinatariosDia += destinatarios;
            return null;
        }
        finally
        {
            _turno.Release();
        }
    }
}
