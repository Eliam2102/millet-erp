namespace Millet.CuentasPorCobrar.Domain.LineaCredito;

/// <summary>
/// Estados de una línea de crédito (§4.1 del 01-diseño). Los valores se
/// persisten como <c>short</c> con check constraint (incidente 2026-07-11);
/// al agregar un valor nuevo: constraint + migration + mirror FE en el
/// mismo PR.
/// </summary>
public enum EstadoLineaCredito : short
{
    Activa = 1,
    Bloqueada = 2,

    /// <summary>
    /// Reservado para suspensión administrativa (p.ej. baja del seguro
    /// SOLUNION sin bloqueo de cartera). Sin transición en CXC-PR1 —
    /// entra con las alertas de cartera (CXC-PR8).
    /// </summary>
    Suspendida = 3,
}
