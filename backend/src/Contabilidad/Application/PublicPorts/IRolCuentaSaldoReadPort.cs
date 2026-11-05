namespace Millet.Contabilidad.Application.PublicPorts;

/// <summary>
/// Cuenta y saldo deudor al corte que el motor usa para un rol (ALM-13-a: rol INVENTARIO). Cada clave (sucursal, clasificador) se
/// resuelve con la misma regla del motor (rol <c>{rol}_{clasificador}</c> con cuenta y, si no, el rol base; el de la sucursal antes
/// que el genérico; MXN; vigente al corte). El saldo es de la cuenta en <paramref name="sucursalesSaldo"/> (null = toda la empresa).
/// <c>CuentaId</c> nulo = sin cuenta configurada para esa clave.
/// </summary>
public interface IRolCuentaSaldoReadPort
{
    Task<IReadOnlyDictionary<(Guid SucursalId, string? Clasificador), SaldoRolSucursal>> ObtenerAsync(string rol, DateOnly corte,
        IReadOnlyCollection<(Guid SucursalId, string? Clasificador)> claves, IReadOnlyCollection<Guid>? sucursalesSaldo, CancellationToken ct);
}

public sealed record SaldoRolSucursal(Guid? CuentaId, string? CuentaCodigo, string? CuentaNombre, decimal? Saldo);
