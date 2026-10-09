using MediatR;
using Millet.CuentasPorPagar.Domain.Ports.Tesoreria;
using Millet.Tesoreria.Application.PublicPorts;
namespace Millet.Api.Infrastructure.Adapters;
public sealed class PagosProveedorReadPortAdapter(ISender sender) : IPagosProveedorReadPort
{
    public async Task<IReadOnlyList<Millet.CuentasPorPagar.Domain.Ports.Tesoreria.PagoProveedorLectura>> ListarAsync(IReadOnlyCollection<Guid> facturas, CancellationToken ct) =>
        (await sender.Send(new ConsultarPagosProveedorQuery(facturas), ct)).Select(p => new Millet.CuentasPorPagar.Domain.Ports.Tesoreria.PagoProveedorLectura(p.EmpresaId, p.FacturaId, p.PagoId, p.Fecha, p.Importe, p.CubiertoRepp, p.Revertido)).ToList();
}
