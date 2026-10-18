using MediatR;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.Application.Recepciones;

// ============================================================================
// Líneas recibibles de una OC (86e3pc50v, P-20). La pantalla de recepción usa
// estos valores en lugar de un porcentaje fijo: el máximo sale de
// RecepcionOcGuard.Limite, el mismo cálculo que valida el registro.
// ============================================================================

public sealed record LineasRecibiblesQuery(Guid OrdenCompraId) : IRequest<IReadOnlyList<LineaRecibibleDto>>;

public sealed record LineaRecibibleDto(
    Guid LineaOcId,
    Guid ArticuloId,
    string UnidadMedida,
    decimal CantidadSolicitada,
    decimal CantidadRecibida,
    decimal ToleranciaPorcentaje,
    decimal Pendiente,
    decimal MaximoRecibible);

public sealed class LineasRecibiblesHandler : IRequestHandler<LineasRecibiblesQuery, IReadOnlyList<LineaRecibibleDto>>
{
    private readonly AlmacenDbContext _db;
    private readonly IComprasOcReadPort _ordenesCompra;
    private readonly IArticuloReadPort _articulos;
    private readonly IConversionUnidadPort _conversion;

    public LineasRecibiblesHandler(
        AlmacenDbContext db, IComprasOcReadPort ordenesCompra, IArticuloReadPort articulos, IConversionUnidadPort conversion)
    {
        _db = db;
        _ordenesCompra = ordenesCompra;
        _articulos = articulos;
        _conversion = conversion;
    }

    public async Task<IReadOnlyList<LineaRecibibleDto>> Handle(LineasRecibiblesQuery request, CancellationToken cancellationToken)
    {
        var oc = await _ordenesCompra.ObtenerAsync(request.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException("RECEPCION_OC_NO_ENCONTRADA", "La orden de compra no existe; selecciona una orden autorizada.");

        var propio = await RecibidoPorLineaOc.CalcularAsync(_db, _conversion, oc, cancellationToken);
        var articuloIds = oc.Lineas.Select(l => l.ArticuloId).Distinct().ToArray();
        var articulos = await _articulos.ObtenerPorIdsAsync(articuloIds, cancellationToken);

        return oc.Lineas.Select(l =>
        {
            var articulo = articulos.GetValueOrDefault(l.ArticuloId);
            var tolerancia = articulo?.ToleranciaCantidadPorcentaje ?? 0m;
            var recibida = Math.Max(l.CantidadRecibida, propio.GetValueOrDefault(l.LineaId));
            return new LineaRecibibleDto(
                l.LineaId,
                l.ArticuloId,
                l.UnidadMedida,
                l.CantidadSolicitada,
                recibida,
                tolerancia,
                Math.Max(0m, l.CantidadSolicitada - recibida),
                Math.Max(0m, RecepcionOcGuard.Limite(l.CantidadSolicitada, l.CantidadRecibida, propio.GetValueOrDefault(l.LineaId), tolerancia)));
        }).ToList();
    }
}
