using MediatR;
using Millet.CuentasPorPagar.Application.FacturaProveedor.Elegibilidad;
using Millet.Tesoreria.Domain.Ports;

namespace Millet.Api.Infrastructure.Adapters;

public sealed class ElegibleFacturaReadPortAdapter(ISender sender) : IElegibleFacturaReadPort
{
    public Task<decimal> ObtenerLimiteAcumuladoAsync(Guid facturaId, CancellationToken cancellationToken) =>
        sender.Send(new ObtenerElegiblePagoQuery(facturaId), cancellationToken);
}
