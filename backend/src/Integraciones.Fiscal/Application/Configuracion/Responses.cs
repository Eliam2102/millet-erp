namespace Millet.Integraciones.Fiscal.Application.Configuracion;

/// <summary>
/// Vista pública de la configuración del PAC para una empresa. El campo
/// <see cref="ApiKey"/> SIEMPRE se enmascara con <c>"••••"</c> — el
/// plaintext nunca sale del backend. <see cref="ApiKeyHash"/> tampoco se
/// expone para evitar canal lateral de detección de rotación desde el
/// frontend (no es estrictamente necesario; el admin UI puede inferir
/// rotación por <see cref="UltimaRotacionAt"/>).
/// </summary>
public sealed record ConfiguracionPacResponse(
    Guid Id,
    Guid EmpresaId,
    short Proveedor,
    string ProveedorNombre,
    string BaseUrl,
    string ApiKey,
    bool ApiKeyConfigured,
    bool Activo,
    DateTimeOffset? UltimaRotacionAt,
    DateTimeOffset? UltimaTestConexionAt,
    bool? UltimaTestConexionExitosa,
    IdentidadSandboxDto? EmisorSandbox,
    IdentidadSandboxDto? ReceptorSandbox,
    bool CsdConfigurado,
    DateTimeOffset? CsdActualizadoAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version);

/// <summary>
/// CSD del emisor para la emisión "por valores" de FiscalAPI
/// (<c>Issuer.TaxCredentials</c>): contenido de los archivos .cer y .key
/// en base64 + password de la llave. Solo viaja de entrada (captura);
/// las respuestas exponen únicamente <c>CsdConfigurado</c>.
/// </summary>
public sealed record CsdDto(
    string CertificadoBase64,
    string LlavePrivadaBase64,
    string Password);

/// <summary>
/// Identidad de prueba (persona de la LCO sintética del SAT) que sustituye
/// al emisor o receptor real cuando la configuración apunta al sandbox de
/// FiscalAPI. Ver docs.fiscalapi.com/testing-data.
/// </summary>
public sealed record IdentidadSandboxDto(
    string Rfc,
    string RazonSocial,
    string RegimenFiscal,
    string CodigoPostal);
