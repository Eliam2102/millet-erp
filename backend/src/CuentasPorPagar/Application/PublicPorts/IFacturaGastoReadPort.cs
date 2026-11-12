namespace Millet.CuentasPorPagar.Application.PublicPorts;
public sealed record FacturaGastoLectura(Guid Id, Guid ProveedorId, Guid SucursalId, string Moneda, DateOnly Fecha, string Referencia, decimal? TipoCambio = null, decimal ImportePagado = 0, decimal SaldoPendiente = decimal.MaxValue, DateOnly? FechaPago = null);
public sealed record FacturaServicioSaldo(Guid Id, Guid EmpresaId, Guid ProveedorId, Guid SucursalId, string Referencia, decimal SaldoPendiente, bool Autorizada, bool PagoPorConciliar = false, string Moneda = "MXN");
public interface IFacturaGastoReadPort
{
    Task<IReadOnlyList<FacturaServicioSaldo>> ObtenerSaldosAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<FacturaGastoLectura?> BuscarAsync(Guid proveedorId, string referencia, Guid? id, CancellationToken ct);
}
