using MediatR;
using Millet.Facturacion.Domain.Ports;
using Millet.Tesoreria.Application.PublicPorts;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>
/// 86e3tfdve: Facturación lee la cuenta bancaria de Millet (dueño: Tesorería)
/// por el puerto público <see cref="ConsultarCuentaBancariaQuery"/>, sin tocar
/// tablas ajenas (mismo patrón que <see cref="PagosProveedorReadPortAdapter"/>).
/// </summary>
public sealed class CuentaBancariaMilletReadAdapter(ISender sender) : ICuentaBancariaMilletReadPort
{
    public async Task<CuentaBancariaMillet?> ObtenerAsync(Guid cuentaBancariaId, CancellationToken cancellationToken) =>
        await sender.Send(new ConsultarCuentaBancariaQuery(cuentaBancariaId), cancellationToken) is { } c
            ? new CuentaBancariaMillet(c.Id, c.Banco, c.NumeroCuenta, c.Clabe, c.Moneda, c.Activa)
            : null;
}
