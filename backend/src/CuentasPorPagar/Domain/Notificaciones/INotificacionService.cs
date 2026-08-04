namespace Millet.CuentasPorPagar.Domain.Notificaciones;

/// <summary>
/// Puerto del servicio transversal de notificaciones (ADR-0026).
/// CxP lo usa desde el <c>RevisionSlaNotificacionWorker</c> para
/// emitir alertas día 3 / día 5 / día 10 a los gerentes y director
/// del área revisora (§A22).
///
/// <para>
/// F4-PR2 introduce el contrato + stub <c>NoOpNotificacionService</c>.
/// PLATFORM-TODO(&lt;NotificacionesGlobal&gt;): wirear adapter real
/// cuando el módulo Notificaciones cross-ERP esté disponible.
/// </para>
/// </summary>
public interface INotificacionService
{
    /// <summary>
    /// Envía una notificación de SLA cumplido. La implementación real
    /// resuelve el destinatario por dependencia + nivel (gerente o
    /// director) y selecciona el canal (email del módulo Notificaciones).
    /// </summary>
    Task NotificarSlaRevisionAsync(SlaRevisionNotificacion notificacion, CancellationToken cancellationToken);
}

public sealed record SlaRevisionNotificacion(
    Guid EmpresaId,
    Guid FacturaProveedorId,
    Guid DependenciaRevisoraId,
    Guid MotivoRevisionId,
    int DiasEnRevision,
    int SlaDias,
    NivelEscalamientoSla Nivel);

public enum NivelEscalamientoSla
{
    /// <summary>Día 3 (o 8 para SLA 15): aviso al gerente del área revisora.</summary>
    AvisoGerente   = 1,

    /// <summary>Día 5 (o 12 para SLA 15): SLA vencido + alerta visible en bandeja.</summary>
    SlaVencido     = 2,

    /// <summary>Día 10 (o 20 para SLA 15): escalamiento al director del área.</summary>
    EscalamientoDirector = 3,
}
