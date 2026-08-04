namespace Millet.Integraciones.Fiscal.Domain.Ports;

/// <summary>
/// Puerto que resuelve la <see cref="ConfiguracionPac"/> activa de una
/// empresa **descifrando** el ApiKey al vuelo. Lo invocan
/// <c>FiscalApiHttpClient</c> y los workers de descarga/refresh (PR-6)
/// para obtener los datos completos antes de hacer la llamada HTTP al
/// PAC.
///
/// <para>
/// <b>Cache</b>: la implementación cachea por <c>(empresaId, proveedor)</c>
/// con TTL 60s usando <c>IMemoryCache</c>. Cuando el admin guarda una
/// configuración nueva (handler de <c>GuardarConfiguracionPacCommand</c>),
/// el handler invoca <see cref="Invalidar"/> para forzar refresh
/// inmediato.
/// </para>
/// </summary>
public interface IConfiguracionPacResolver
{
    /// <summary>
    /// Devuelve el snapshot resuelto de la configuración para
    /// <paramref name="empresaId"/> + <paramref name="proveedor"/>, o
    /// <c>null</c> si no hay configuración activa.
    /// </summary>
    Task<ConfiguracionPacResuelta?> ResolverAsync(
        Guid empresaId,
        ProveedorPac proveedor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invalida el cache para la empresa + proveedor. Lo invoca el
    /// handler de guardar/rotar para que el próximo
    /// <see cref="ResolverAsync"/> vea los datos frescos.
    /// </summary>
    void Invalidar(Guid empresaId, ProveedorPac proveedor);
}

/// <summary>
/// Snapshot de la configuración con ApiKey (y CSD, si está capturado) ya
/// descifrados. Inmutable — si la fila en BD cambia, el resolver
/// re-cachea con el TTL vencido.
/// </summary>
public sealed record ConfiguracionPacResuelta(
    Guid ConfiguracionId,
    Guid EmpresaId,
    ProveedorPac Proveedor,
    string BaseUrl,
    string ApiKey,
    bool Activo,
    IdentidadSandboxResuelta? EmisorSandbox = null,
    IdentidadSandboxResuelta? ReceptorSandbox = null,
    CsdResuelto? Csd = null);

/// <summary>
/// CSD del emisor descifrado para la emisión por valores
/// (<c>Issuer.TaxCredentials</c> de FiscalAPI). Vive solo en el cache en
/// memoria del resolver (TTL 60s), nunca se serializa ni se loggea.
/// </summary>
public sealed record CsdResuelto(
    string CertificadoBase64,
    string LlavePrivadaBase64,
    string Password);

/// <summary>
/// Copia plana de <see cref="IdentidadSandbox"/> para el snapshot cacheado
/// del resolver (el snapshot no arrastra entidades del agregado).
/// </summary>
public sealed record IdentidadSandboxResuelta(
    string Rfc,
    string RazonSocial,
    string RegimenFiscal,
    string CodigoPostal);
