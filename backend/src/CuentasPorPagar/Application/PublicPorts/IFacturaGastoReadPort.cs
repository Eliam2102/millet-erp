namespace Millet.CuentasPorPagar.Application.PublicPorts;
public sealed record FacturaGastoLectura(Guid Id, Guid ProveedorId, Guid SucursalId, string Moneda, DateOnly Fecha, string Referencia, decimal? TipoCambio = null, decimal ImportePagado = 0, decimal SaldoPendiente = decimal.MaxValue, DateOnly? FechaPago = null);
public interface IFacturaGastoReadPort
{
    Task<FacturaGastoLectura?> BuscarAsync(Guid proveedorId, string referencia, Guid? id, CancellationToken ct);
}
