using Millet.Almacen.Domain.Ports;
using Millet.Contabilidad.Application.PublicPorts;

namespace Millet.Almacen.Infrastructure.PublicAdapters;

/// <summary>Lee la cuenta del rol INVENTARIO y su saldo por el puerto público de Contabilidad (ALM-13-a).</summary>
public sealed class InventarioContableReadAdapter(IRolCuentaSaldoReadPort roles) : IInventarioContableReadPort
{
    public const string RolInventario = "INVENTARIO";

    public async Task<IReadOnlyDictionary<Guid, CuentaInventarioSaldo>> ObtenerAsync(DateOnly corte, IReadOnlyCollection<Guid> sucursales, CancellationToken ct) =>
        (await roles.ObtenerAsync(RolInventario, corte, sucursales, ct))
            .ToDictionary(x => x.Key, x => new CuentaInventarioSaldo(x.Value.CuentaId, x.Value.CuentaCodigo, x.Value.CuentaNombre, x.Value.Saldo));
}
