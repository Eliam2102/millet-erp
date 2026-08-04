namespace Millet.Compras.Domain.Trazabilidad;

/// <summary>
/// Nodo del árbol de trazabilidad cross-módulo (F7-PR2). Cada nodo
/// referencia un documento y enlaza a sus ascendentes (upstream, e.g.
/// las RQs que originaron una OC) y descendentes (downstream, e.g.
/// las OCs creadas desde una RQ).
///
/// <para>
/// V1 solo expone RQ ↔ OC. Cuando un módulo nuevo implementa su
/// provider, agrega entries en las listas correspondientes. La
/// estructura es agnóstica: el frontend renderiza la cadena completa
/// sin saber qué módulos están conectados.
/// </para>
/// </summary>
public sealed record NodoArbolDocumento(
    TipoDocumentoTrazabilidad TipoDocumento,
    Guid Id,
    string Folio,
    string Estado,
    DateTimeOffset Fecha,
    IReadOnlyList<NodoArbolDocumento> Ascendentes,
    IReadOnlyList<NodoArbolDocumento> Descendentes);
