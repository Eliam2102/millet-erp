using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Infrastructure;

/// <summary>TR-01: versión del pedido A+W que el ERP ya aplicó (<c>ingesta_control</c>). Lectura pura, para Integraciones.Aw.</summary>
public sealed class AwVersionPedidoReadAdapter(FacturacionDbContext db, ICurrentEmpresaContext empresa) : IAwVersionPedidoReadPort
{
    public async Task<long?> VersionAplicadaAsync(string numeroPedido, CancellationToken cancellationToken)
    {
        using var bypass = empresa.Bypass(); // corre en un worker, sin empresa de request
        return await db.IngestaControles.AsNoTracking()
            .Where(c => c.Origen == OrigenPedido.Aw && c.ClaveNatural == numeroPedido)
            .Select(c => (long?)c.UltimaVersionAplicada)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
