using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.PublicPorts;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
namespace Millet.Api.Infrastructure.Adapters;

public sealed class FacturaServicioCeReadAdapter(CuentasPorPagarDbContext db) : IFacturaServicioCeReadPort
{
    public Task<bool> TienePasivoVigenteAsync(Guid empresaId, Guid ordenCompraId, CancellationToken ct) =>
        db.FacturasProveedor.AsNoTracking().AnyAsync(f => f.EmpresaId == empresaId && f.OrdenCompraId == ordenCompraId
            && f.EsServicioComercioExterior && f.Estado != EstadoPasivo.Cancelada, ct);
}
