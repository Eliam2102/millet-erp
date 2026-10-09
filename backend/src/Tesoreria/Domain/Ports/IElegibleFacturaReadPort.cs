namespace Millet.Tesoreria.Domain.Ports;

public interface IElegibleFacturaReadPort
{
    Task<decimal> ObtenerLimiteAcumuladoAsync(Guid facturaId, CancellationToken cancellationToken);
}
