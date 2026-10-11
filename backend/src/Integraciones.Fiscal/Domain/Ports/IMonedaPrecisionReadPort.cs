namespace Millet.Integraciones.Fiscal.Domain.Ports;
public interface IMonedaPrecisionReadPort
{
    Task<IReadOnlyDictionary<string, int>> ObtenerAsync(IReadOnlyCollection<string> monedas, CancellationToken ct);
}
