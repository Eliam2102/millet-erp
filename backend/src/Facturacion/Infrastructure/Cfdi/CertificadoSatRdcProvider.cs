using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Millet.Facturacion.Application.Timbrado.Adopcion;

namespace Millet.Facturacion.Infrastructure.Cfdi;

/// <summary>
/// M-284 (QA O1): certificado del SAT por número, del repositorio público de certificados del SAT
/// (<c>rdc.sat.gob.mx/rccf/...</c>). Se guarda en memoria: un certificado no cambia por número.
/// </summary>
public sealed class CertificadoSatRdcProvider(HttpClient http, ILogger<CertificadoSatRdcProvider>? logger = null)
    : ICertificadoSatProvider
{
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new(StringComparer.Ordinal);

    public async Task<X509Certificate2?> ObtenerAsync(string noCertificado, CancellationToken cancellationToken)
    {
        var no = noCertificado.Trim();
        if (no.Length != 20 || !no.All(char.IsAsciiDigit)) return null;
        if (!Cache.TryGetValue(no, out var bytes))
        {
            var url = $"https://rdc.sat.gob.mx/rccf/{no[..6]}/{no[6..12]}/{no[12..14]}/{no[14..16]}/{no[16..18]}/{no}.cer";
            try
            {
                using var respuesta = await http.GetAsync(url, cancellationToken);
                if (!respuesta.IsSuccessStatusCode)
                {
                    logger?.LogWarning("Certificado del SAT {NoCertificado} no disponible ({Status}).", no, (int)respuesta.StatusCode);
                    return null;
                }
                bytes = await respuesta.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger?.LogWarning(ex, "No se pudo descargar el certificado del SAT {NoCertificado}.", no);
                return null;
            }
            Cache[no] = bytes;
        }
        try
        {
            return X509CertificateLoader.LoadCertificate(bytes);
        }
        catch (CryptographicException)
        {
            Cache.TryRemove(no, out _);
            return null;
        }
    }
}
