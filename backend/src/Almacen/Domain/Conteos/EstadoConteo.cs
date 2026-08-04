namespace Millet.Almacen.Domain.Conteos;

/// <summary>
/// Ciclo de vida de un <see cref="ConteoInventario"/> (01-diseno §5.1).
/// <list type="bullet">
///   <item><see cref="Planificado"/> — creado, sin snapshot todavía.</item>
///   <item><see cref="EnCurso"/> — snapshot tomado, captura habilitada
///   (sin sesgo: contador NO ve cantidad teórica).</item>
///   <item><see cref="EnConciliacion"/> — captura completada, aprobador
///   compara teórico vs real.</item>
///   <item><see cref="Aprobado"/> — aprobador firmó ajustes (política A8).</item>
///   <item><see cref="Aplicado"/> — movimientos AjustePositivo/AjusteNegativo
///   generados; saldos actualizados.</item>
///   <item><see cref="Rechazado"/> — terminal por error/anomalía.</item>
/// </list>
/// </summary>
public enum EstadoConteo : short
{
    Planificado = 0,
    EnCurso = 1,
    EnConciliacion = 2,
    Aprobado = 3,
    Aplicado = 4,
    Rechazado = 5,
}

/// <summary>
/// Tipo de conteo (01-diseno §7).
/// <list type="bullet">
///   <item><see cref="Rotativo"/> — sin bloqueo de salidas; sub-conjunto
///   del catálogo según calendario.</item>
///   <item><see cref="Anual"/> — bloqueo de salidas durante la captura
///   (A18, F7-PR3).</item>
/// </list>
/// </summary>
public enum TipoConteo : short
{
    Rotativo = 0,
    Anual = 1,
}
