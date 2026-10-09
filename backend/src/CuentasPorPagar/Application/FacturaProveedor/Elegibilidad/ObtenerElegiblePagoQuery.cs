using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.Elegibilidad;

public sealed record ObtenerElegiblePagoQuery(Guid FacturaId) : IRequest<decimal>;

/// <summary>Límite acumulado antes de pagos; Tesorería descuenta sus pagos firmes, incluso los aún no proyectados en CxP.</summary>
public sealed class ObtenerElegiblePagoHandler(CuentasPorPagarDbContext db, ElegibilidadFacturaService elegibilidad)
    : IRequestHandler<ObtenerElegiblePagoQuery, decimal>
{
    public async Task<decimal> Handle(ObtenerElegiblePagoQuery request, CancellationToken cancellationToken)
    {
        var factura = await db.FacturasProveedor.Include(f => f.Lineas).FirstOrDefaultAsync(f => f.Id == request.FacturaId, cancellationToken);
        if (factura is null || factura.Estado is not (EstadoPasivo.Autorizada or EstadoPasivo.Pagada)) return 0;
        var resultado = await elegibilidad.CalcularAsync(factura, cancellationToken);
        return Math.Max(0, resultado.ElegibleTotal - factura.AnticipoAplicadoTotal);
    }
}
