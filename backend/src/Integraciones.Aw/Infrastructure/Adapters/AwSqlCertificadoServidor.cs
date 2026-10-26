using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Millet.Integraciones.Aw.Infrastructure.Adapters;

/// <summary>
/// Confianza en el certificado público de SER-DATA (ADR-0048): SqlClient solo verifica contra el
/// almacén de confianza, así que el PEM (<c>IntegracionesAw:SqlTls:CertificadoServidorPem</c>, KV
/// <c>aw-sql-server-certificate</c>) se agrega una vez a <c>CurrentUser\Root</c>. Nunca se registra el PEM.
/// </summary>
public sealed class AwSqlCertificadoServidor
{
    public const string ConfigKey = "IntegracionesAw:SqlTls:CertificadoServidorPem";
    public static readonly TimeSpan AvisoVencimiento = TimeSpan.FromDays(60);

    public X509Certificate2? Certificado { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Parsea el PEM y lo instala en el almacén si falta (idempotente). No lanza: el estado queda en <see cref="Error"/>.</summary>
    public static AwSqlCertificadoServidor Instalar(string pem, StoreLocation ubicacion = StoreLocation.CurrentUser)
    {
        var r = new AwSqlCertificadoServidor();
        try
        {
            var cert = X509Certificate2.CreateFromPem(pem);
            using var store = new X509Store(StoreName.Root, ubicacion);
            store.Open(OpenFlags.ReadWrite);
            if (store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, validOnly: false).Count == 0)
                store.Add(cert);
            r.Certificado = cert;
        }
        catch (Exception ex)
        {
            r.Error = $"No se pudo instalar el certificado de SER-DATA ({ex.GetType().Name}).";
        }
        return r;
    }

    public HealthCheckResult Evaluar(DateTimeOffset ahora)
    {
        if (Certificado is null) return HealthCheckResult.Unhealthy(Error ?? "Certificado de SER-DATA no cargado.");
        var vence = new DateTimeOffset(Certificado.NotAfter.ToUniversalTime());
        if (vence <= ahora) return HealthCheckResult.Unhealthy($"El certificado de SER-DATA venció el {vence:yyyy-MM-dd}.");
        if (vence - ahora < AvisoVencimiento) return HealthCheckResult.Degraded($"El certificado de SER-DATA vence el {vence:yyyy-MM-dd}; cargue el nuevo en KV aw-sql-server-certificate.");
        return HealthCheckResult.Healthy($"Certificado de SER-DATA vigente hasta {vence:yyyy-MM-dd}.");
    }
}

public sealed class AwSqlCertificadoServidorHealthCheck(AwSqlCertificadoServidor cert) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(cert.Evaluar(DateTimeOffset.UtcNow));
}

/// <summary>Deja en el log subject, huella y vencimiento del certificado instalado.</summary>
public sealed class AwSqlCertificadoServidorLogger(AwSqlCertificadoServidor cert, ILogger<AwSqlCertificadoServidorLogger> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (cert.Certificado is { } c)
            logger.LogInformation("Certificado SER-DATA confiado: {Subject} huella {Thumbprint} vence {NotAfter:O}", c.Subject, c.Thumbprint, c.NotAfter);
        else
            logger.LogError("Certificado SER-DATA inválido: {Error}", cert.Error);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
