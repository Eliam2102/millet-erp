namespace Millet.Facturacion.Domain.Ports;

public interface IReppBancarioReadPort
{
    Task<decimal?> TipoCambioAsync(string moneda, DateOnly fecha, CancellationToken cancellationToken);
    Task<Guid> SucursalEmisoraAsync(CancellationToken cancellationToken);
}
