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
    /// Envía una notificación de SLA cumplido. La implementación resuelve los
    /// destinatarios (Contador General siempre; Compras si
    /// <see cref="SlaRevisionNotificacion.AvisaCompras"/>) y el canal.
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
    NivelEscalamientoSla Nivel,
    string? Folio,
    string MotivoNombre,
    bool AvisaCompras);

public enum NivelEscalamientoSla
{
    /// <summary>Día hábil 3 (o 8 para SLA 15): primer aviso.</summary>
    AvisoGerente   = 1,

    /// <summary>Día hábil 5 (o 12 para SLA 15): SLA vencido; alerta visible en bandeja.</summary>
    SlaVencido     = 2,

    /// <summary>Día 10 hábil (20 para SLA 15): último escalamiento (Dirección de Finanzas pendiente de destinatario, PLATFORM-TODO(&lt;NotificacionesGlobal&gt;)).</summary>
    EscalamientoDirector = 3,
}
