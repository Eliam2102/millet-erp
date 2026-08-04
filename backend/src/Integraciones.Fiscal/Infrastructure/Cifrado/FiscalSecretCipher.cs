using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Millet.Integraciones.Fiscal.Infrastructure.Cifrado;

/// <summary>
/// Cifrador del <c>ApiKey</c> de FiscalAPI usando ASP.NET DataProtection
/// (ADR-0037). El DEK (Data Encryption Key) vive en Azure Key Vault y el
/// ring de keys que rota cada 90 días vive en Azure Blob Storage; cuando
/// no están configurados, ASP.NET DataProtection cae automáticamente a
/// filesystem en <c>%LOCALAPPDATA%\ASP.NET\DataProtection-Keys</c> (dev
/// local sin Azure).
///
/// <para>
/// <b>Purpose string</b>: <c>Integraciones.Fiscal.ApiKey.v1</c>. Es parte
/// del contrato de cifrado — cambiarlo rompe el descifrado de los
/// ciphertexts ya persistidos. Si surge necesidad de rotar el algoritmo
/// (no la DEK, eso es rotación automática del ring), bump a
/// <c>v2</c> y migrar las filas existentes.
/// </para>
///
/// <para>
/// <b>Aislamiento</b>: cada módulo de integraciones tiene su propio
/// cipher con purpose distinto (Mailbox NO usa este — usa cert-based
/// auth y no requiere cifrado de columna). Esto da margen para rotar uno
/// sin tocar el otro.
/// </para>
/// </summary>
public sealed class FiscalSecretCipher
{
    /// <summary>
    /// Purpose string del <see cref="IDataProtector"/>. Constante pública
    /// para que los tests verifiquen el contrato.
    /// </summary>
    public const string Purpose = "Integraciones.Fiscal.ApiKey.v1";

    private readonly IDataProtector _protector;

    public FiscalSecretCipher(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    /// <summary>
    /// Cifra el plaintext UTF-8 del ApiKey y retorna el ciphertext como
    /// <c>byte[]</c> persistible en columna <c>bytea</c>.
    /// </summary>
    public byte[] Encrypt(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        return _protector.Protect(Encoding.UTF8.GetBytes(plaintext));
    }

    /// <summary>
    /// Descifra el ciphertext persistido y retorna el plaintext UTF-8.
    /// Lanza <see cref="CryptographicException"/> si el ciphertext está
    /// corrupto, fue cifrado con otro purpose, o el ring de keys no
    /// reconoce la versión usada (típico si el DEK fue purgado de KV).
    /// </summary>
    public string Decrypt(byte[] ciphertext)
    {
        if (ciphertext is null || ciphertext.Length == 0)
            throw new ArgumentException("Ciphertext vacío o nulo.", nameof(ciphertext));
        return Encoding.UTF8.GetString(_protector.Unprotect(ciphertext));
    }

    /// <summary>
    /// SHA256 hex del plaintext, usado para detectar rotación sin
    /// descifrar. El handler de
    /// <c>GuardarConfiguracionPacCommand</c> (PR-4) compara este hash
    /// contra el persistido en <c>ConfiguracionPac.ApiKeyHash</c>:
    /// si coincide, no re-cifra; si difiere, rota.
    ///
    /// <para>
    /// El hash NO compromete confidencialidad: SHA256 es preimage-resistant
    /// + el plaintext del ApiKey es ≥32 chars random (espacio de búsqueda
    /// computacionalmente intratable).
    /// </para>
    /// </summary>
    public static string HashForChangeDetection(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)))
            .ToLowerInvariant();
    }
}
