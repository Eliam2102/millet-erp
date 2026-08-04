namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura hacia Cuentas por Pagar para resolver referencias de
/// documentos CxP a etiquetas legibles (ADR-0042). Primer puerto de lectura
/// Almacén → CxP; hasta ahora la relación era solo por eventos (triada).
///
/// <para>
/// Lo usan las queries de presentación de Almacén: el detalle de recepción
/// referencia <c>CfdiRecibidoId</c> (variante A) y <c>FacturaId</c>
/// (variante B), y el detalle de devolución 8.B referencia
/// <c>NotaCreditoFiscalId</c> — los tres son GUIDs internos de CxP que no
/// deben pintarse crudos. <b>Solo lectura de presentación</b>: no habilita
/// lógica de negocio cruzada (la conciliación sigue viajando por eventos).
/// </para>
///
/// <para>
/// Mismo contrato batch que <see cref="IComprasOcReadPort.ObtenerFoliosAsync"/>:
/// ids distintos, una consulta, claves no encontradas ausentes del
/// diccionario (el consumidor hace fallback al id truncado).
/// </para>
/// </summary>
public interface ICxpDocumentosReadPort
{
    /// <summary>
    /// <c>facturaId → folio del proveedor</c> (serie-folio formateado).
    /// Facturas sin folio del proveedor no aparecen en el diccionario.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosFacturaAsync(
        IReadOnlyCollection<Guid> facturaIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// <c>cfdiRecibidoId → UUID fiscal del SAT</c> (folio fiscal del CFDI,
    /// dato fiscal legítimo de cara al usuario).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> ObtenerUuidsFiscalesCfdiAsync(
        IReadOnlyCollection<Guid> cfdiRecibidoIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// <c>notaCreditoId → folio del proveedor</c> (serie-folio formateado).
    /// NCs sin folio del proveedor no aparecen en el diccionario.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosNotaCreditoAsync(
        IReadOnlyCollection<Guid> notaCreditoIds,
        CancellationToken cancellationToken);
}
