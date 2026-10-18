using System.Text.Json.Serialization;

namespace Millet.Compras.Domain;

/// <summary>
/// Qué condiciones de la requisición exigen nivel 2 de autorización
/// (además de la naturaleza Crítico/Riesgo, que siempre exige N2).
///
/// <list type="bullet">
///   <item><see cref="SoloMonto"/> (default): en la requisición significa
///         «sin criterio de disponibilidad»: la RQ sale solo por naturaleza
///         (N1). El monto contra el umbral del departamento se valida en la
///         orden de compra (decisión de negocio, minuta Millet 12-oct).
///         No se renombra el valor por compatibilidad de datos.</item>
///   <item><see cref="SoloDisponibilidad"/>: exige N2 si alguna línea con
///         artículo de inventario no tiene existencia suficiente en la
///         sucursal (la compra quedaría por cubrir).</item>
///   <item><see cref="Ambos"/>: en la requisición equivale a
///         <see cref="SoloDisponibilidad"/> (el monto ya no aplica a la RQ).</item>
/// </list>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModoNivel2
{
    SoloMonto = 1,
    SoloDisponibilidad = 2,
    Ambos = 3,
}
