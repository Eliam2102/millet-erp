using Millet.Almacen.Domain.Ports;
using Millet.Contabilidad.Application.PublicPorts;

namespace Millet.Almacen.Infrastructure.PublicAdapters;

/// <summary>Lee la cuenta del rol INVENTARIO por grupo de artículo y su saldo por el puerto público de Contabilidad (ALM-13-a).</summary>
public sealed class InventarioContableReadAdapter(IRolCuentaSaldoReadPort roles) : IInventarioContableReadPort
{
    public const string RolInventario = "INVENTARIO";

    public async Task<IReadOnlyDictionary<(Guid SucursalId, string? Grupo), CuentaInventarioSaldo>> ObtenerAsync(DateOnly corte,
        IReadOnlyCollection<(Guid SucursalId, string? Grupo)> claves, IReadOnlyCollection<Guid>? sucursalesSaldo, CancellationToken ct) =>
        (await roles.ObtenerAsync(RolInventario, corte, claves, sucursalesSaldo, ct))
            .ToDictionary(x => x.Key, x => new CuentaInventarioSaldo(x.Value.CuentaId, x.Value.CuentaCodigo, x.Value.CuentaNombre, x.Value.Saldo));
}
