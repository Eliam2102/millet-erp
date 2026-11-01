using System.Formats.Asn1;
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
///   <item>El subject contiene OU de sucursal/unidad (CSD del SAT, no FIEL).</item>
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
    public static VigenciaCsd Validar(
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

        using var certificado = cert;
        if (!TieneUnidadOrganizativa(cert.SubjectName))
            throw new BusinessRuleException("CONFIG_PAC_CSD_ES_FIEL",
                "Este archivo es una e.firma (FIEL), no un sello digital (CSD)");

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

        var notBefore = new DateTimeOffset(cert.NotBefore.ToUniversalTime());
        var notAfter = new DateTimeOffset(cert.NotAfter.ToUniversalTime());
        if (ahora < notBefore || ahora > notAfter)
        {
            throw new BusinessRuleException("CONFIG_PAC_CSD_VENCIDO",
                $"El certificado no está vigente (válido del {cert.NotBefore:yyyy-MM-dd} al " +
                $"{cert.NotAfter:yyyy-MM-dd}). Captura un CSD vigente.");
        }

        return new VigenciaCsd(notBefore, notAfter, NumeroSerie(cert), Rfc(cert.SubjectName));
    }

    /// <summary>
    /// F6.9: datos públicos de un .cer ya guardado (número de serie, RFC y
    /// vigencia) para registrar en el historial los CSD capturados antes de él.
    /// No valida vigencia ni llave.
    /// </summary>
    public static VigenciaCsd LeerMetadatos(string certificadoBase64)
    {
        using var cert = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(certificadoBase64));
        return new VigenciaCsd(
            new DateTimeOffset(cert.NotBefore.ToUniversalTime()),
            new DateTimeOffset(cert.NotAfter.ToUniversalTime()),
            NumeroSerie(cert),
            Rfc(cert.SubjectName));
    }

    /// <summary>
    /// Número de certificado del SAT: el serial X.509 son los códigos ASCII de
    /// los 20 dígitos (p. ej. <c>3330…</c> → <c>30…</c>). Si no tiene esa forma,
    /// se deja el serial hexadecimal tal cual.
    /// </summary>
    internal static string NumeroSerie(X509Certificate2 cert)
    {
        var hex = cert.SerialNumber;
        if (hex.Length % 2 != 0) return hex;
        var chars = new char[hex.Length / 2];
        for (var i = 0; i < chars.Length; i++)
        {
            var b = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            if (b is < (byte)'0' or > (byte)'9') return hex;
            chars[i] = (char)b;
        }
        return new string(chars);
    }

    /// <summary>RFC del titular: atributo x500UniqueIdentifier (2.5.4.45), «RFC / RFC del representante».</summary>
    internal static string? Rfc(X500DistinguishedName subject)
    {
        var valor = Atributo(subject, "2.5.4.45");
        return valor?.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    private static string? Atributo(X500DistinguishedName subject, string oidBuscado)
    {
        var nombre = new AsnReader(subject.RawData, AsnEncodingRules.DER).ReadSequence();
        while (nombre.HasData)
        {
            var rdn = nombre.ReadSetOf();
            while (rdn.HasData)
            {
                var atributo = rdn.ReadSequence();
                var oid = atributo.ReadObjectIdentifier();
                var valor = atributo.ReadCharacterString((UniversalTagNumber)atributo.PeekTag().TagValue);
                if (oid == oidBuscado && !string.IsNullOrWhiteSpace(valor)) return valor.Trim();
            }
        }
        return null;
    }

    private static bool TieneUnidadOrganizativa(X500DistinguishedName subject)
    {
        // Leer el OID real evita confundir un CN que contenga el texto "OU="
        // con una unidad organizativa, y admite RDN con varios atributos.
        var nombre = new AsnReader(subject.RawData, AsnEncodingRules.DER).ReadSequence();
        while (nombre.HasData)
        {
            var rdn = nombre.ReadSetOf();
            while (rdn.HasData)
            {
                var atributo = rdn.ReadSequence();
                var oid = atributo.ReadObjectIdentifier();
                if (oid == "2.5.4.11")
                {
                    var valor = atributo.ReadCharacterString((UniversalTagNumber)atributo.PeekTag().TagValue);
                    if (!string.IsNullOrWhiteSpace(valor)) return true;
                }
            }
        }
        return false;
    }
}

/// <summary>Vigencia y datos públicos del CSD (F6.9: número de serie y RFC para el historial).</summary>
public sealed record VigenciaCsd(DateTimeOffset NotBefore, DateTimeOffset NotAfter, string NumeroSerie = "", string? Rfc = null);
