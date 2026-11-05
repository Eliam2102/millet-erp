namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// ALM-13-a: cuenta de inventario y su saldo al corte por (sucursal, grupo de artículo). Contabilidad decide la cuenta con la misma
/// regla con la que contabiliza los movimientos de almacén (rol <c>INVENTARIO_{grupo}</c> con cuenta y, si no, <c>INVENTARIO</c>).
/// El saldo es de la cuenta en <c>sucursalesSaldo</c> (null = toda la empresa). <c>CuentaId</c> nulo = sin cuenta configurada.
/// </summary>
public interface IInventarioContableReadPort
{
    Task<IReadOnlyDictionary<(Guid SucursalId, string? Grupo), CuentaInventarioSaldo>> ObtenerAsync(DateOnly corte,
        IReadOnlyCollection<(Guid SucursalId, string? Grupo)> claves, IReadOnlyCollection<Guid>? sucursalesSaldo, CancellationToken ct);
}

public sealed record CuentaInventarioSaldo(Guid? CuentaId, string? CuentaCodigo, string? CuentaNombre, decimal? Saldo);
