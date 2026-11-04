namespace Millet.Contabilidad.Application.PublicPorts;

/// <summary>
/// Saldo deudor al corte de la cuenta que el motor usa para un rol en cada sucursal (ALM-13-a: rol INVENTARIO). La cuenta sale con la
/// misma regla que el motor al generar la póliza (rol de la sucursal antes que el genérico, MXN, vigente al corte); el saldo solo
/// cuenta las partidas de esa sucursal. <c>CuentaId</c> nulo = el rol no tiene cuenta configurada para la sucursal.
/// </summary>
public interface IRolCuentaSaldoReadPort
{
    Task<IReadOnlyDictionary<Guid, SaldoRolSucursal>> ObtenerAsync(string rol, DateOnly corte, IReadOnlyCollection<Guid> sucursales, CancellationToken ct);
}

public sealed record SaldoRolSucursal(Guid? CuentaId, string? CuentaCodigo, string? CuentaNombre, decimal? Saldo);
