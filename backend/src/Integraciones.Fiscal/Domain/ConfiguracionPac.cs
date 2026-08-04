using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Integraciones.Fiscal.Domain;

/// <summary>
/// Agregado raíz de la configuración de PAC por empresa
/// (ADR-0038, levantamiento §6). Una fila por
/// <c>(empresa_id, proveedor)</c>; en MVP solo hay un proveedor
/// (FiscalAPI), así que efectivamente es una fila por empresa.
///
/// <para>
/// <b>Cifrado del ApiKey</b>: el atributo <see cref="ApiKeyCifrado"/> es
/// el ciphertext producido por ASP.NET DataProtection con DEK en Key
/// Vault (ADR-0037). El dominio NO conoce el plaintext — el resolver
/// (Infrastructure) descifra al inyectar en el HttpClient.
/// </para>
///
/// <para>
/// <b>Detección de rotación</b>: <see cref="ApiKeyHash"/> es SHA256 del
/// plaintext, calculado por el handler de <c>GuardarConfiguracionPacCommand</c>
/// (PR-4). Permite saber si el caller mandó la misma key (idempotente,
/// no se re-cifra ni se actualiza <see cref="UltimaRotacionAt"/>) o una
/// nueva (rota).
/// </para>
///
/// <para>
/// <b>Multi-empresa</b>: implementa <see cref="IPerteneceAEmpresa"/>;
/// el query filter global del <c>BaseDbContext</c> lo aísla por
/// <c>EmpresaContext</c>. Los workers de fondo levantan
/// <c>ICurrentEmpresaContext.Bypass()</c> para iterar todas las
/// configuraciones activas.
/// </para>
/// </summary>
public sealed class ConfiguracionPac : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public ProveedorPac Proveedor { get; private set; }
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>Ciphertext del ApiKey producido por DataProtection (ADR-0037).</summary>
    public byte[] ApiKeyCifrado { get; private set; } = [];

    /// <summary>SHA256 hex del plaintext para detección de rotación sin descifrar.</summary>
    public string ApiKeyHash { get; private set; } = string.Empty;

    public bool Activo { get; private set; }
    public DateTimeOffset? UltimaRotacionAt { get; private set; }
    public DateTimeOffset? UltimaTestConexionAt { get; private set; }
    public bool? UltimaTestConexionExitosa { get; private set; }

    /// <summary>
    /// Identidad de pruebas que sustituye al EMISOR real en el payload hacia
    /// el PAC. Invariante: solo puede existir cuando <see cref="EsSandbox"/>
    /// — un deploy a prod con BaseUrl live jamás timbra con RFC de prueba.
    /// </summary>
    public IdentidadSandbox? EmisorSandbox { get; private set; }

    /// <summary>Identidad de pruebas que sustituye al RECEPTOR real (misma invariante que <see cref="EmisorSandbox"/>).</summary>
    public IdentidadSandbox? ReceptorSandbox { get; private set; }

    /// <summary>
    /// Contenido base64 del certificado CSD (<c>.cer</c>) cifrado con
    /// DataProtection (mismo esquema que <see cref="ApiKeyCifrado"/>,
    /// ADR-0037). La emisión "por valores" de FiscalAPI exige enviar el CSD
    /// en cada timbrado (<c>Issuer.TaxCredentials</c>); el adapter lo
    /// descifra vía el resolver. En sandbox es el CSD de prueba del SAT
    /// (docs.fiscalapi.com/testing-data); en live, el CSD real de la empresa.
    /// </summary>
    public byte[]? CsdCertificadoCifrado { get; private set; }

    /// <summary>Contenido base64 de la llave privada CSD (<c>.key</c>) cifrado con DataProtection.</summary>
    public byte[]? CsdLlavePrivadaCifrada { get; private set; }

    /// <summary>Password de la llave privada CSD cifrado con DataProtection.</summary>
    public byte[]? CsdPasswordCifrado { get; private set; }

    /// <summary>SHA256 hex del plaintext combinado (cer|key|password) para detección de rotación sin descifrar.</summary>
    public string? CsdHash { get; private set; }

    public DateTimeOffset? CsdActualizadoAt { get; private set; }

    /// <summary>Las tres piezas del CSD están capturadas — condición para timbrar por valores.</summary>
    public bool CsdConfigurado =>
        CsdCertificadoCifrado is { Length: > 0 }
        && CsdLlavePrivadaCifrada is { Length: > 0 }
        && CsdPasswordCifrado is { Length: > 0 };
    // PR-13: timeouts/retry/CB y schedule de descarga/refresh quedaron
    // obsoletos al adoptar el SDK NuGet oficial Fiscalapi (PR-9), que
    // maneja resiliencia internamente, y los workers asíncronos (PR-11)
    // que toman intervalos de configuración global (IntegracionesFiscal:
    // Workers:{Submitter,Poller}). Se eliminaron del agregado.

    private ConfiguracionPac() { } // EF Core

    public ConfiguracionPac(
        Guid id,
        Guid empresaId,
        ProveedorPac proveedor,
        string baseUrl,
        byte[] apiKeyCifrado,
        string apiKeyHash,
        DateTimeOffset ahora) : base(id)
    {
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("CONFIG_PAC_EMPRESA_INVALIDA",
                "EmpresaId es requerida.");
        ValidarBaseUrl(baseUrl);
        ValidarApiKey(apiKeyCifrado, apiKeyHash);

        EmpresaId = empresaId;
        Proveedor = proveedor;
        BaseUrl = baseUrl;
        ApiKeyCifrado = apiKeyCifrado;
        ApiKeyHash = apiKeyHash;
        Activo = true;
        UltimaRotacionAt = ahora;
    }

    /// <summary>
    /// Rota el ApiKey (y opcionalmente la BaseUrl). Idempotente: si
    /// <paramref name="apiKeyHash"/> coincide con el actual, no toca nada.
    /// </summary>
    public void RotarApiKey(byte[] nuevoCifrado, string nuevoHash, DateTimeOffset ahora)
    {
        ValidarApiKey(nuevoCifrado, nuevoHash);
        if (nuevoHash == ApiKeyHash) return; // idempotente

        ApiKeyCifrado = nuevoCifrado;
        ApiKeyHash = nuevoHash;
        UltimaRotacionAt = ahora;
    }

    public void ActualizarBaseUrl(string baseUrl)
    {
        ValidarBaseUrl(baseUrl);
        BaseUrl = baseUrl;

        // Salir de sandbox limpia las identidades de prueba: la invariante
        // "identidades ⇒ sandbox" nunca se rompe, ni siquiera transitoriamente.
        if (!EsSandbox)
        {
            EmisorSandbox = null;
            ReceptorSandbox = null;
        }
    }

    /// <summary>
    /// <c>true</c> cuando la BaseUrl apunta al ambiente de pruebas de
    /// FiscalAPI (<c>test.fiscalapi.com</c>). Único host donde se permiten
    /// identidades sandbox — cualquier otro (incluido live) las rechaza.
    /// </summary>
    public bool EsSandbox =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
        && string.Equals(uri.Host, "test.fiscalapi.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Asigna (o limpia con <c>null</c>) las identidades de prueba que
    /// sustituyen emisor/receptor en el payload al PAC. Rechaza la captura
    /// si la configuración no apunta al sandbox.
    /// </summary>
    public void ConfigurarIdentidadesSandbox(IdentidadSandbox? emisor, IdentidadSandbox? receptor)
    {
        if ((emisor is not null || receptor is not null) && !EsSandbox)
            throw new BusinessRuleException("CONFIG_PAC_IDENTIDAD_REQUIERE_SANDBOX",
                "Las identidades de prueba solo se permiten con BaseUrl del sandbox (test.fiscalapi.com).");

        EmisorSandbox = emisor;
        ReceptorSandbox = receptor;
    }

    /// <summary>
    /// Captura o rota el CSD del emisor (las tres piezas viajan juntas —
    /// FiscalAPI exige exactamente cer + key con el mismo password).
    /// Idempotente por <paramref name="hash"/>: si coincide con el actual,
    /// no re-cifra ni actualiza <see cref="CsdActualizadoAt"/>. El CSD NO
    /// se limpia al cambiar de ambiente: al pasar a live debe rotarse al
    /// CSD real (un CSD de prueba en live falla visible en el PAC).
    /// </summary>
    public void ConfigurarCsd(
        byte[] certificadoCifrado,
        byte[] llavePrivadaCifrada,
        byte[] passwordCifrado,
        string hash,
        DateTimeOffset ahora)
    {
        if (certificadoCifrado is not { Length: > 0 }
            || llavePrivadaCifrada is not { Length: > 0 }
            || passwordCifrado is not { Length: > 0 })
            throw new BusinessRuleException("CONFIG_PAC_CSD_INCOMPLETO",
                "El CSD requiere certificado (.cer), llave privada (.key) y password, cifrados.");
        if (string.IsNullOrWhiteSpace(hash) || hash.Length != 64)
            throw new BusinessRuleException("CONFIG_PAC_CSD_HASH_INVALIDO",
                "CsdHash debe ser SHA256 hex de 64 caracteres.");

        if (hash == CsdHash) return; // idempotente

        CsdCertificadoCifrado = certificadoCifrado;
        CsdLlavePrivadaCifrada = llavePrivadaCifrada;
        CsdPasswordCifrado = passwordCifrado;
        CsdHash = hash;
        CsdActualizadoAt = ahora;
    }

    /// <summary>Elimina el CSD capturado (p.ej. credencial comprometida). Idempotente.</summary>
    public void LimpiarCsd()
    {
        CsdCertificadoCifrado = null;
        CsdLlavePrivadaCifrada = null;
        CsdPasswordCifrado = null;
        CsdHash = null;
        CsdActualizadoAt = null;
    }

    /// <summary>Activa la configuración. Idempotente.</summary>
    public void Activar() => Activo = true;

    /// <summary>Desactiva sin borrar — los workers la omiten en próximos ticks.</summary>
    public void Desactivar() => Activo = false;

    public void RegistrarTestConexion(bool exitosa, DateTimeOffset ahora)
    {
        UltimaTestConexionAt = ahora;
        UltimaTestConexionExitosa = exitosa;
    }

    private static void ValidarBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new BusinessRuleException("CONFIG_PAC_BASE_URL_INVALIDA",
                "BaseUrl es requerida.");
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme is not "http" and not "https"))
            throw new BusinessRuleException("CONFIG_PAC_BASE_URL_INVALIDA",
                "BaseUrl debe ser una URI absoluta http o https.");
        if (baseUrl.Length > 500)
            throw new BusinessRuleException("CONFIG_PAC_BASE_URL_INVALIDA",
                "BaseUrl excede 500 caracteres.");
    }

    private static void ValidarApiKey(byte[] cifrado, string hash)
    {
        if (cifrado is null || cifrado.Length == 0)
            throw new BusinessRuleException("CONFIG_PAC_APIKEY_INVALIDA",
                "ApiKey cifrado es requerido (resultado de DataProtection).");
        if (string.IsNullOrWhiteSpace(hash) || hash.Length != 64)
            throw new BusinessRuleException("CONFIG_PAC_APIKEY_HASH_INVALIDO",
                "ApiKeyHash debe ser SHA256 hex de 64 caracteres.");
    }
}
