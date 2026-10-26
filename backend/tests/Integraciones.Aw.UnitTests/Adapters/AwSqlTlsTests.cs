using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Millet.Integraciones.Aw.Infrastructure.Adapters;

namespace Millet.Integraciones.Aw.UnitTests.Adapters;

public sealed class AwSqlTlsTests
{
    private const string Base = "Server=aw-sql-onprem,1433;Database=X;User ID=u;Password=p;";

    [Theory]
    [InlineData("Encrypt=True;TrustServerCertificate=False")]
    [InlineData("Encrypt=Mandatory")]
    [InlineData("TrustServerCertificate=False")] // Encrypt por omisión = Mandatory en SqlClient 5
    public void Validar_acepta_tls_verificado(string sufijo)
        => AwSqlTls.Validar(Base + sufijo, "T").Should().NotBeNullOrEmpty();

    [Theory]
    [InlineData("Encrypt=False")]
    [InlineData("Encrypt=Optional")]
    [InlineData("Encrypt=True;TrustServerCertificate=True")]
    public void Validar_rechaza_cifrado_debil_o_sin_verificar(string sufijo)
        => FluentActions.Invoking(() => AwSqlTls.Validar(Base + sufijo, "AwX"))
            .Should().Throw<InvalidOperationException>().WithMessage("*AwX*TLS verificado*");

    [Fact]
    public void Validar_rechaza_Strict_por_TDS8()
        => FluentActions.Invoking(() => AwSqlTls.Validar(Base + "Encrypt=Strict", "AwX"))
            .Should().Throw<InvalidOperationException>().WithMessage("*TDS 8*");

    [Fact]
    public void Validar_no_filtra_la_contrasena_en_el_mensaje()
        => FluentActions.Invoking(() => AwSqlTls.Validar(Base + "Encrypt=False", "AwX"))
            .Should().Throw<InvalidOperationException>().Which.Message.Should().NotContain("Password").And.NotContain("=p;");

    private static (string Pem, X509Certificate2 Cert) Generar(DateTimeOffset desde, DateTimeOffset hasta)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN=test-{Guid.NewGuid():N}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(desde, hasta);
        return (cert.ExportCertificatePem(), cert);
    }

    private static AwSqlCertificadoServidor InstalarYLimpiar(string pem, X509Certificate2 cert, out AwSqlCertificadoServidor segunda)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        try
        {
            var primera = AwSqlCertificadoServidor.Instalar(pem);
            segunda = AwSqlCertificadoServidor.Instalar(pem); // idempotente
            store.Open(OpenFlags.ReadWrite);
            store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false).Should().HaveCount(1);
            return primera;
        }
        finally
        {
            store.Open(OpenFlags.ReadWrite);
            store.Remove(cert);
        }
    }

    [Fact]
    public void Certificado_vigente_es_Healthy_y_se_instala_una_sola_vez()
    {
        var (pem, cert) = Generar(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        var r = InstalarYLimpiar(pem, cert, out var segunda);
        r.Error.Should().BeNull();
        segunda.Error.Should().BeNull();
        r.Evaluar(DateTimeOffset.UtcNow).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void Certificado_por_vencer_es_Degraded_y_vencido_es_Unhealthy()
    {
        var (pem, cert) = Generar(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(30));
        var r = InstalarYLimpiar(pem, cert, out _);
        r.Evaluar(DateTimeOffset.UtcNow).Status.Should().Be(HealthStatus.Degraded);
        r.Evaluar(DateTimeOffset.UtcNow.AddDays(31)).Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public void Pem_invalido_es_Unhealthy_sin_lanzar_ni_filtrar_el_pem()
    {
        var r = AwSqlCertificadoServidor.Instalar("-----BEGIN CERTIFICATE-----\nbasura-secreta\n-----END CERTIFICATE-----");
        r.Certificado.Should().BeNull();
        var h = r.Evaluar(DateTimeOffset.UtcNow);
        h.Status.Should().Be(HealthStatus.Unhealthy);
        h.Description.Should().NotContain("basura-secreta");
    }
}
