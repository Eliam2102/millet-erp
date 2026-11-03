using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Millet.Integraciones.Aw.Infrastructure.Adapters;

/// <summary>
/// Confianza en el certificado público de SER-DATA (ADR-0048). El PEM (<c>IntegracionesAw:SqlTls:CertificadoServidorPem</c>,
/// KV <c>aw-sql-server-certificate</c>) se escribe en un archivo local y cada conexión lo fija con <c>ServerCertificate</c>
/// (<see cref="AwSqlTls.Validar"/>). No se usa <c>CurrentUser\Root</c>: en App Service Linux vive en <c>/home</c>, un
/// recurso compartido cifs cuyo dueño no es el proceso, y .NET no deja escribir ahí (CryptographicException, 29-oct).
/// Nunca se registra el PEM.
/// </summary>
public sealed class AwSqlCertificadoServidor
{
    public const string ConfigKey = "IntegracionesAw:SqlTls:CertificadoServidorPem";
    public static readonly TimeSpan AvisoVencimiento = TimeSpan.FromDays(60);

    public X509Certificate2? Certificado { get; private set; }
    public string? Ruta { get; private set; }
    public string? Error { get; private set; }

    /// <summary>
    /// Parsea el PEM, lo escribe en <paramref name="directorio"/> (por omisión, el temporal local del contenedor) y lo
    /// deja como certificado fijado de las conexiones A+W (idempotente). No lanza: el estado queda en <see cref="Error"/>.
    /// </summary>
    public static AwSqlCertificadoServidor Instalar(string pem, string? directorio = null)
    {
        var r = new AwSqlCertificadoServidor();
        try
        {
            var cert = X509Certificate2.CreateFromPem(pem);
            var dir = directorio ?? Path.Combine(Path.GetTempPath(), "millet-aw-tls");
            Directory.CreateDirectory(dir);
            var ruta = Path.Combine(dir, "ser-data.pem");
            File.WriteAllText(ruta, cert.ExportCertificatePem());
            AwSqlTls.CertificadoServidorRuta = ruta;
            r.Certificado = cert;
            r.Ruta = ruta;
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
