using MediatR;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.Application.Recepciones;

// ============================================================================
// Líneas recibibles de una OC (86e3pc50v, P-20). La pantalla de recepción usa
// estos valores: el máximo (= pendiente, sin tolerancia en piezas) sale de
// RecepcionOcGuard.Limite, el mismo cálculo que valida el registro.
// ============================================================================

public sealed record LineasRecibiblesQuery(Guid OrdenCompraId) : IRequest<IReadOnlyList<LineaRecibibleDto>>;

public sealed record LineaRecibibleDto(
    Guid LineaOcId,
    Guid ArticuloId,
    string UnidadMedida,
    decimal CantidadSolicitada,
    decimal CantidadRecibida,
    decimal Pendiente,
    decimal MaximoRecibible,
    // F1-MP-01 (E1): medida de hoja que fijó la OC en esta línea (null = línea sin medida). No se elige al recibir.
    MedidaLineaOcDto? Medida = null);

public sealed record MedidaLineaOcDto(Guid VarianteId, decimal AnchoMm, decimal AltoMm, decimal AreaHojaM2);

public sealed class LineasRecibiblesHandler : IRequestHandler<LineasRecibiblesQuery, IReadOnlyList<LineaRecibibleDto>>
{
    private readonly AlmacenDbContext _db;
    private readonly IComprasOcReadPort _ordenesCompra;
    private readonly IConversionUnidadPort _conversion;

    public LineasRecibiblesHandler(AlmacenDbContext db, IComprasOcReadPort ordenesCompra, IConversionUnidadPort conversion)
    {
        _db = db;
        _ordenesCompra = ordenesCompra;
        _conversion = conversion;
    }

    public async Task<IReadOnlyList<LineaRecibibleDto>> Handle(LineasRecibiblesQuery request, CancellationToken cancellationToken)
    {
        var oc = await _ordenesCompra.ObtenerAsync(request.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException("RECEPCION_OC_NO_ENCONTRADA", "La orden de compra no existe; selecciona una orden autorizada.");

        var propio = await RecibidoPorLineaOc.CalcularAsync(_db, _conversion, oc, cancellationToken);

        return oc.Lineas.Select(l =>
        {
            var recibida = Math.Max(l.CantidadRecibida, propio.GetValueOrDefault(l.LineaId));
            return new LineaRecibibleDto(
                l.LineaId,
                l.ArticuloId,
                l.UnidadMedida,
                l.CantidadSolicitada,
                recibida,
                Math.Max(0m, l.CantidadSolicitada - recibida),
                Math.Max(0m, RecepcionOcGuard.Limite(l.CantidadSolicitada, l.CantidadRecibida, propio.GetValueOrDefault(l.LineaId))),
                l is { ProductoAwVarianteId: { } v, AnchoMm: { } ancho, AltoMm: { } alto } ? new MedidaLineaOcDto(v, ancho, alto, AreaHoja.M2(ancho, alto)) : null);
        }).ToList();
    }
}
