namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// ALM-13-a: saldo al corte de la cuenta de inventario (rol <c>INVENTARIO</c> de Contabilidad) por sucursal. Contabilidad decide la
/// cuenta con la misma regla con la que contabiliza los movimientos de almacén. <c>CuentaId</c> nulo = sin cuenta configurada.
/// </summary>
public interface IInventarioContableReadPort
{
    Task<IReadOnlyDictionary<Guid, CuentaInventarioSaldo>> ObtenerAsync(DateOnly corte, IReadOnlyCollection<Guid> sucursales, CancellationToken ct);
}

public sealed record CuentaInventarioSaldo(Guid? CuentaId, string? CuentaCodigo, string? CuentaNombre, decimal? Saldo);
