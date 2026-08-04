using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Millet.Integraciones.Fiscal.Infrastructure.Cifrado;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.UnitTests.Infrastructure;

/// <summary>
/// Material CSD sintético para tests: certificado self-signed + llave
/// privada PKCS#8 cifrada — el mismo formato que los .cer/.key del SAT.
/// </summary>
internal static class CsdTestFactory
{
    public static (string CerBase64, string KeyBase64, string Password) Crear(
        string password = "12345678a",
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=EKU9003173C9", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(
            notBefore ?? DateTimeOffset.UtcNow.AddDays(-1),
            notAfter ?? DateTimeOffset.UtcNow.AddYears(4));

        var cerBase64 = Convert.ToBase64String(cert.Export(X509ContentType.Cert));
        var keyBase64 = Convert.ToBase64String(rsa.ExportEncryptedPkcs8PrivateKey(
            password,
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 10_000)));
        return (cerBase64, keyBase64, password);
    }
}

public sealed class CsdValidadorTests
{
    private static readonly DateTimeOffset Ahora = DateTimeOffset.UtcNow;

    [Fact]
    public void Csd_valido_pasa()
    {
        var (cer, key, pass) = CsdTestFactory.Crear();

        var act = () => CsdValidador.Validar(cer, key, pass, Ahora);

        act.Should().NotThrow();
    }

    [Fact]
    public void Password_incorrecta_es_rechazada()
    {
        var (cer, key, _) = CsdTestFactory.Crear(password: "correcta");

        var act = () => CsdValidador.Validar(cer, key, "incorrecta", Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONFIG_PAC_CSD_PASSWORD_INCORRECTA");
    }

    [Fact]
    public void Llave_de_otro_par_es_rechazada()
    {
        var (cer, _, _) = CsdTestFactory.Crear();
        var (_, otraKey, otraPass) = CsdTestFactory.Crear();

        var act = () => CsdValidador.Validar(cer, otraKey, otraPass, Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONFIG_PAC_CSD_NO_CORRESPONDEN");
    }

    [Fact]
    public void Certificado_vencido_es_rechazado()
    {
        var (cer, key, pass) = CsdTestFactory.Crear(
            notBefore: DateTimeOffset.UtcNow.AddYears(-5),
            notAfter: DateTimeOffset.UtcNow.AddDays(-1));

        var act = () => CsdValidador.Validar(cer, key, pass, Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONFIG_PAC_CSD_VENCIDO");
    }

    [Fact]
    public void Cer_que_no_es_certificado_es_rechazado()
    {
        var (_, key, pass) = CsdTestFactory.Crear();
        var noEsCert = Convert.ToBase64String("esto no es un certificado"u8.ToArray());

        var act = () => CsdValidador.Validar(noEsCert, key, pass, Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONFIG_PAC_CSD_CERTIFICADO_INVALIDO");
    }

    [Fact]
    public void Base64_invalido_es_rechazado()
    {
        var act = () => CsdValidador.Validar("no-base64!!!", "tampoco###", "x", Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONFIG_PAC_CSD_BASE64_INVALIDO");
    }
}
