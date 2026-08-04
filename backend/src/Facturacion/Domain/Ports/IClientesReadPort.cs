namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Puerto de lectura del master de Cliente (dueño: <c>DatosMaestros</c>).
/// Facturación resuelve los datos fiscales del receptor (RFC, régimen, CP
/// fiscal, defaults) sin tocar el esquema de DatosMaestros — el ERP es master
/// único y los clientes nacen desde A+W por auto-provisión (§3.bis.6 del
/// diseño). La auto-provisión es un puerto distinto (<c>IMasterProvisioningPort</c>,
/// F3); este puerto solo lee.
/// </summary>
public interface IClientesReadPort
{
    /// <summary>Resuelve un cliente por su id interno del ERP.</summary>
    Task<ClienteFiscalLectura?> ObtenerAsync(Guid clienteId, CancellationToken cancellationToken);

    /// <summary>
    /// Resuelve un cliente por su referencia externa (p.ej. <c>numero_cliente</c>
    /// de A+W). Devuelve <c>null</c> si no existe en el master — la
    /// auto-provisión es responsabilidad de <c>IMasterProvisioningPort</c>, no
    /// de este puerto.
    /// </summary>
    Task<ClienteFiscalLectura?> ResolverPorReferenciaAsync(
        string referenciaExterna,
        CancellationToken cancellationToken);

    /// <summary>
    /// Búsqueda de clientes activos para el selector del formulario de emisión
    /// (FAC-UX-PR1, cierra PLATFORM-TODO(&lt;ClienteSelector&gt;)). Filtros
    /// excluyentes al estilo ADR-0045: RFC (substring) o razón social
    /// (substring con folding de acentos). Sin filtros devuelve los primeros
    /// <paramref name="limit"/> por clave.
    /// </summary>
    Task<IReadOnlyList<ClienteBusquedaItem>> BuscarAsync(
        string? rfc,
        string? razonSocial,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// Item de búsqueda para el selector de cliente en emisión: incluye la clave
/// (display) y los defaults fiscales para autollenar el formulario al
/// seleccionar.
/// </summary>
public sealed record ClienteBusquedaItem(
    Guid ClienteId,
    string Clave,
    string RazonSocial,
    string? Rfc,
    string? RegimenFiscal,
    string? CodigoPostalFiscal,
    string? UsoCfdiDefault,
    string? FormaPagoDefault,
    string? MetodoPagoDefault,
    string MonedaDefault,
    bool EsGenerico,
    string? NumRegIdTrib,
    string? PaisResidencia,
    string? DomicilioExtranjeroCalle,
    string? DomicilioExtranjeroEstado,
    string? DomicilioExtranjeroCodigoPostal);

/// <summary>
/// Snapshot de los datos fiscales del cliente al momento de la consulta. Datos
/// fiscales incompletos (RFC/régimen/CP en <c>null</c>) <b>no</b> bloquean la
/// importación del pedido pero sí el timbrado (validación al emitir, §1
/// levantamiento).
/// </summary>
public sealed record ClienteFiscalLectura(
    Guid ClienteId,
    string? Rfc,
    string RazonSocial,
    string? RegimenFiscal,
    string? CodigoPostalFiscal,
    string? UsoCfdiDefault,
    string? FormaPagoDefault,
    string? MetodoPagoDefault,
    string MonedaDefault,
    bool EsGenerico);
