namespace Millet.Almacen.Domain.Ports.Notificaciones;

/// <summary>
/// Puerto de salida hacia el módulo de Notificaciones (ADR-0026, futuro).
/// Lo consumen workers como <c>RegularizacionValeSlaWorker</c> (F5-PR1,
/// A14) para avisar al Coordinador y al Jefe de Almacén que un vale
/// urgente sigue sin regularizar tras 24h / 48h.
///
/// <para>
/// Stub <c>NoOpNotificacionService</c> en F0-PR1 — logea pero no
/// dispara email/SignalR todavía. PLATFORM-TODO al adapter real
/// cuando el módulo Notificaciones entre.
/// </para>
/// </summary>
public interface INotificacionService
{
    Task NotificarValeSinRegularizarAsync(
        Guid movimientoValeId,
        string folioVale,
        Guid? personaDestinatariaId,
        DateTimeOffset fechaLimite,
        int diaDelSla,
        CancellationToken cancellationToken);
}
