using MediatR;
using Millet.Facturacion.Domain.Ports;
using Millet.Tesoreria.Application.PublicPorts;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>
/// 86e3tfdve (opción A): Facturación lee la cuenta ordenante del movimiento bancario
/// (dueño: Tesorería) por el puerto público <see cref="ConsultarCuentaOrdenanteMovimientoQuery"/>,
/// mismo patrón que <see cref="CuentaBancariaMilletReadAdapter"/>.
/// </summary>
public sealed class CuentaOrdenanteMovimientoReadAdapter(ISender sender) : ICuentaOrdenanteMovimientoReadPort
{
    public Task<string?> ObtenerAsync(Guid movimientoBancarioId, CancellationToken cancellationToken) =>
        sender.Send(new ConsultarCuentaOrdenanteMovimientoQuery(movimientoBancarioId), cancellationToken);
}
