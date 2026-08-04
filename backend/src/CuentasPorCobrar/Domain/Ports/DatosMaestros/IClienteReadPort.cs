namespace Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;

/// <summary>
/// Referencia mínima del master de clientes (ADR-0048 D6) que CxC
/// necesita para correlacionar y mostrar (§6.1 del 01-diseño).
/// </summary>
public sealed record ClienteRefDto(
    Guid Id,
    string? Rfc,
    string RazonSocial,
    bool EsGenerico);

/// <summary>
/// Item del lookup de clientes para bandejas y selectores del frontend
/// CxC (CXC-FE-PR2). Incluye <c>Clave</c> para el combobox ADR-0045.
/// </summary>
public sealed record ClienteLookupCxcDto(
    Guid Id,
    string Clave,
    string? Rfc,
    string RazonSocial);

/// <summary>
/// Puerto de lectura del master de clientes de DatosMaestros (CXC-PR3).
/// La proyección <c>factura_cartera</c> lo usa para resolver
/// RFC → ClienteId (decisión 2026-07-13: los eventos de Facturación
/// traen el snapshot fiscal del receptor, no un ClienteId).
/// </summary>
public interface IClienteReadPort
{
    /// <summary>
    /// Resuelve un cliente por RFC exacto (case-insensitive, sin
    /// genéricos). <c>null</c> si no hay match — la factura queda en
    /// cartera sin cliente vinculado.
    /// </summary>
    Task<ClienteRefDto?> ObtenerPorRfcAsync(string rfc, CancellationToken cancellationToken);

    /// <summary>Obtiene la referencia del cliente por Id (bandejas/reportes).</summary>
    Task<ClienteRefDto?> ObtenerAsync(Guid clienteId, CancellationToken cancellationToken);

    /// <summary>
    /// Búsqueda para el lookup del frontend (CXC-FE-PR2): por RFC
    /// (prefijo, precedencia ADR-0045), razón social (contains,
    /// case-insensitive) o set de ids (resolución de nombres de una
    /// página de bandeja). Excluye genéricos — el público general no
    /// participa del crédito.
    /// </summary>
    Task<IReadOnlyList<ClienteLookupCxcDto>> BuscarAsync(
        string? rfc,
        string? razonSocial,
        IReadOnlyCollection<Guid>? ids,
        int limit,
        CancellationToken cancellationToken);
}
