namespace Millet.Integraciones.Fiscal.Domain.Ports;

/// <summary>Prueba credenciales transitorias; no persiste ni cachea el candidato.</summary>
public interface IPacCandidatoProbe
{
    Task<PingResultDto> ProbarAsync(string baseUrl, string apiKey, CancellationToken cancellationToken);
}
