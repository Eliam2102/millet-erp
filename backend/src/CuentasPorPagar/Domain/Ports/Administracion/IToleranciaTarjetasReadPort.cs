namespace Millet.CuentasPorPagar.Domain.Ports.Administracion;

public interface IToleranciaTarjetasReadPort
{
    Task<decimal> ObtenerMontoMxnAsync(CancellationToken cancellationToken);
}
