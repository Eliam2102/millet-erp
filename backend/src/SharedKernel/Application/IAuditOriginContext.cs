namespace Millet.SharedKernel.Application;

/// <summary>
/// Identifica qué proceso en background está escribiendo cuando no hay
/// <c>HttpContext</c> (worker de integración, bootstrap, seed). Resuelve el
/// hueco de atribución descrito en F1-ADM-03: sin esto, todas las
/// escrituras de todos los procesos en background quedan indistinguibles
/// entre sí en <c>core.audit_log</c> (mismo <c>UsuarioId = null</c>) y en
/// <c>CreatedBy</c>/<c>UpdatedBy</c> (mismo literal <c>"system"</c>).
///
/// Mismo mecanismo que <see cref="ICurrentEmpresaContext.Bypass"/>
/// (<c>AsyncLocal</c> + <c>IDisposable</c>), aplicado a un problema
/// estructuralmente idéntico. Restringido por convención a clases
/// <c>IHostedService</c>/<c>BackgroundService</c> (enforcement en
/// <c>AuditOriginUsageGuardTest</c>).
/// </summary>
public interface IAuditOriginContext
{
    /// <summary>Nombre del proceso en background actual; null si no aplica.</summary>
    string? Origin { get; }

    /// <summary>
    /// Declara que las escrituras dentro del scope vienen del proceso en
    /// background <paramref name="origin"/> (usar <c>nameof(LaClaseDelWorker)</c>).
    /// Disposable: usar en bloque <c>using</c>.
    /// </summary>
    IDisposable SetOrigin(string origin);
}
