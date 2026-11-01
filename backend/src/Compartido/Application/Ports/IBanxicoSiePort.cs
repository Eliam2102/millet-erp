namespace Millet.Compartido.Application.Ports;

/// <summary>
/// Consulta de series del SIE de Banxico (K10.6). Devuelve, por clave de serie, los valores
/// por fecha de determinación; las fechas sin dato («N/E») no vienen. Si Banxico no responde,
/// lanza la excepción del transporte: el worker la trata como falla y reintenta.
/// </summary>
public interface IBanxicoSiePort
{
    Task<IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, decimal>>> ObtenerSeriesAsync(
        IReadOnlyCollection<string> series, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken);
}
