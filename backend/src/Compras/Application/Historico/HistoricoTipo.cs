namespace Millet.Compras.Application.Historico;

/// <summary>
/// Tipos de eventos en el histórico de una requisición (B.2). Mapean
/// las entradas de <c>core.audit_log</c> filtradas por
/// <c>aggregate_root_id = requisicionId</c> a etiquetas legibles para
/// el frontend.
///
/// <para>
/// El mapeo lo hace <see cref="HistoricoTipoMapper"/> en el endpoint
/// (read-time) inspeccionando <c>(Operacion, Entidad, Cambios diff)</c>.
/// Cuando un cambio no encaja en ninguna categoría conocida, se usa
/// <see cref="Cambio"/> como fallback (no es error).
/// </para>
/// </summary>
public enum HistoricoTipo
{
    Cambio = 0,
    Creada = 1,
    LineaAgregada = 2,
    LineaActualizada = 3,
    LineaEliminada = 4,
    Transmitida = 5,
    AutorizadaN1 = 6,
    AutorizadaN2 = 7,
    Rechazada = 8,
    Eliminada = 9,
    Cancelada = 10,
    CubrimientoRegistrado = 11,
    RecepcionRegistrada = 12,
    SaldoNoSurtido = 13,
    Cerrada = 14,
}
