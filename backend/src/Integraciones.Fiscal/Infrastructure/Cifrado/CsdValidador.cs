using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.Infrastructure.Cifrado;

/// <summary>
/// Valida el trío CSD (.cer + .key + password) EN EL GUARDADO, antes de
/// cifrar y persistir — sin esto, una contraseña equivocada se descubre
/// hasta el timbrado con el críptico "The .KEY's password is incorrect"
/// de FiscalAPI (incidente 2026-07-11, COTT-2026-000002 /
/// FACANT-2026-000002). Verifica, en orden:
/// <list type="number">
///   <item>El .cer parsea como certificado X.509 (DER del SAT).</item>
///   <item>La contraseña abre la llave privada (PKCS#8 cifrado — formato
///   estándar de los .key del SAT).</item>
///   <item>La llave corresponde al certificado (misma llave pública).</item>
///   <item>El certificado está vigente a la fecha de captura.</item>
/// </list>
/// Solo valida — el payload persistido sigue siendo el base64 original de
/// los archivos, intacto para <c>Issuer.TaxCredentials</c>.
/// </summary>
public static class CsdValidador
{
    public static void Validar(
        string certificadoBase64, string llavePrivadaBase64, string password, DateTimeOffset ahora)
    {
        byte[] cerBytes;
        byte[] keyBytes;
        try
        {
            cerBytes = Convert.FromBase64String(certificadoBase64);
            keyBytes = Convert.FromBase64String(llavePrivadaBase64);
        }
        catch (FormatException)
        {
            throw new BusinessRuleException("CONFIG_PAC_CSD_BASE64_INVALIDO",
                "El certificado o la llave privada no son base64 válido.");
        }

        X509Certificate2 cert;
        try
        {
            cert = X509CertificateLoader.LoadCertificate(cerBytes);
        }
        catch (CryptographicException)
        {
            throw new BusinessRuleException("CONFIG_PAC_CSD_CERTIFICADO_INVALIDO",
                "El archivo .cer no es un certificado X.509 válido. Verifica que sea el .cer del CSD (no el .key).");
        }

        using var rsa = RSA.Create();
        try
        {
            rsa.ImportEncryptedPkcs8PrivateKey(password, keyBytes, out _);
        }
        catch (CryptographicException)
        {
            // El mismo error cubre password incorrecta y .key corrupto/otro
            // formato — indistinguibles sin abrir la llave.
            throw new BusinessRuleException("CONFIG_PAC_CSD_PASSWORD_INCORRECTA",
                "La contraseña no abre la llave privada (.key). Verifica la contraseña del CSD " +
                "y que el archivo sea el .key correcto.");
        }

        using var certKey = cert.GetRSAPublicKey();
        if (certKey is null
            || !certKey.ExportSubjectPublicKeyInfo().AsSpan()
                .SequenceEqual(rsa.ExportSubjectPublicKeyInfo()))
        {
            throw new BusinessRuleException("CONFIG_PAC_CSD_NO_CORRESPONDEN",
                "La llave privada (.key) no corresponde al certificado (.cer) — son de pares distintos. " +
                "Verifica que ambos archivos sean del mismo CSD.");
        }

        if (ahora < cert.NotBefore.ToUniversalTime() || ahora > cert.NotAfter.ToUniversalTime())
        {
            throw new BusinessRuleException("CONFIG_PAC_CSD_VENCIDO",
                $"El certificado no está vigente (válido del {cert.NotBefore:yyyy-MM-dd} al " +
                $"{cert.NotAfter:yyyy-MM-dd}). Captura un CSD vigente.");
        }
    }
}
