namespace Millet.Administracion.Domain;

/// <summary>
/// Política de reinicio del contador de folios de una <see cref="Serie"/>
/// (F-Admin-PR6.1). Define cada cuánto el contador secuencial vuelve a 1
/// dentro de una serie.
///
/// <list type="bullet">
///   <item><see cref="None"/>: el contador nunca reinicia. Todos los folios
///         de la serie comparten una sola secuencia ascendente.</item>
///   <item><see cref="Anual"/>: el contador reinicia el 1° de enero. La
///         clave de período es el año (<c>"2026"</c>).</item>
///   <item><see cref="Mensual"/>: el contador reinicia el 1° de cada mes.
///         La clave de período es <c>"YYYY-MM"</c> (<c>"2026-05"</c>).</item>
/// </list>
///
/// El valor numérico (<c>short</c>) está fijo por ABI — agregar valores
/// nuevos al final, nunca renumerar (mismo patrón que otros enums del
/// repo persistidos via <c>HasConversion&lt;short&gt;()</c>).
/// </summary>
public enum ReinicioPeriodo : short
{
    None = 0,
    Anual = 1,
    Mensual = 2,
}
