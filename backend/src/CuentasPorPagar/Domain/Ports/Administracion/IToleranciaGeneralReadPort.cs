namespace Millet.CuentasPorPagar.Domain.Ports.Administracion;

public interface IToleranciaGeneralReadPort
{
    Task<decimal> ObtenerMontoMxnAsync(CancellationToken cancellationToken);
}
