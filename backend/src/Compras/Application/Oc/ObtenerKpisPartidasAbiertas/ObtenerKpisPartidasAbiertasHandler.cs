using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Oc.ObtenerKpisPartidasAbiertas;

public sealed class ObtenerKpisPartidasAbiertasHandler
    : IRequestHandler<ObtenerKpisPartidasAbiertasQuery, KpisPartidasAbiertasResponse>
{
    private static readonly EstadoOrdenCompra[] EstadosTerminales =
    [
        EstadoOrdenCompra.Cerrada,
        EstadoOrdenCompra.Cancelada,
        EstadoOrdenCompra.Rechazada,
    ];

    private readonly ComprasDbContext _db;
    private readonly IClock _clock;

    public ObtenerKpisPartidasAbiertasHandler(ComprasDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<KpisPartidasAbiertasResponse> Handle(
        ObtenerKpisPartidasAbiertasQuery request, CancellationToken cancellationToken)
    {
        // ADR-0040 (D2): "atrasada" se mide contra el día de hoy en hora
        // local de México (por día de calendario), no contra el instante UTC.
        var hoy = _clock.HoyLocal();

        var query = _db.OrdenesCompra
            .AsNoTracking()
            .Where(o => !EstadosTerminales.Contains(o.Estado))
            .Where(o =>
                o.SubEstadoRecepcion != SubEstadoRecepcion.Completa
                || o.SubEstadoFacturacion != SubEstadoFacturacion.Completa
                || o.SubEstadoPago != SubEstadoPago.Pagada);

        if (request.ProveedorId is Guid p) query = query.Where(o => o.ProveedorId == p);
        if (request.CompradorTitularId is Guid c) query = query.Where(o => o.CompradorTitularId == c);
        if (request.FechaDocumentoDesde is DateOnly desde) query = query.Where(o => o.FechaDocumento >= desde);
        if (request.FechaDocumentoHasta is DateOnly hasta) query = query.Where(o => o.FechaDocumento <= hasta);

        // Una sola pasada con agregaciones condicionales para evitar 5
        // queries separadas. EF Core 9 traduce SUM(CASE WHEN ...) bien.
        var agregados = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Atrasadas = g.Count(o => o.FechaEntregaEsperada != null && o.FechaEntregaEsperada < hoy),
                ConRecepcionParcial = g.Count(o => o.SubEstadoRecepcion == SubEstadoRecepcion.Parcial),
                ConFacturacionParcial = g.Count(o => o.SubEstadoFacturacion == SubEstadoFacturacion.Parcial),
                SinPago = g.Count(o => o.SubEstadoPago == SubEstadoPago.SinPago),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new KpisPartidasAbiertasResponse(
            CountPartidasAbiertas: agregados?.Total ?? 0,
            CountAtrasadas: agregados?.Atrasadas ?? 0,
            CountConRecepcionParcial: agregados?.ConRecepcionParcial ?? 0,
            CountConFacturacionParcial: agregados?.ConFacturacionParcial ?? 0,
            CountSinPago: agregados?.SinPago ?? 0);
    }
}
