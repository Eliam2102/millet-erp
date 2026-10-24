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
    // F1-MP-01: artículo de vidrio ligado a A+W ⇒ se recibe por medida de hoja (vacío = sin medidas).
    IReadOnlyList<VarianteVidrioLectura>? Variantes = null);

public sealed class LineasRecibiblesHandler : IRequestHandler<LineasRecibiblesQuery, IReadOnlyList<LineaRecibibleDto>>
{
    private readonly AlmacenDbContext _db;
    private readonly IComprasOcReadPort _ordenesCompra;
    private readonly IConversionUnidadPort _conversion;
    private readonly IArticuloReadPort? _articulos;
    private readonly IVarianteVidrioReadPort? _variantes;

    public LineasRecibiblesHandler(
        AlmacenDbContext db, IComprasOcReadPort ordenesCompra, IConversionUnidadPort conversion,
        IArticuloReadPort? articulos = null, IVarianteVidrioReadPort? variantes = null)
    {
        _articulos = articulos;
        _variantes = variantes;
        _db = db;
        _ordenesCompra = ordenesCompra;
        _conversion = conversion;
    }

    public async Task<IReadOnlyList<LineaRecibibleDto>> Handle(LineasRecibiblesQuery request, CancellationToken cancellationToken)
    {
        var oc = await _ordenesCompra.ObtenerAsync(request.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException("RECEPCION_OC_NO_ENCONTRADA", "La orden de compra no existe; selecciona una orden autorizada.");

        var propio = await RecibidoPorLineaOc.CalcularAsync(_db, _conversion, oc, cancellationToken);

        var ligados = _articulos is null || _variantes is null ? new Dictionary<Guid, ArticuloLectura>()
            : (await _articulos.ObtenerPorIdsAsync(oc.Lineas.Select(l => l.ArticuloId).Distinct().ToArray(), cancellationToken))
                .Where(a => a.Value.ProductoAwId is not null).ToDictionary(a => a.Key, a => a.Value);
        var variantesPorArticulo = new Dictionary<Guid, IReadOnlyList<VarianteVidrioLectura>>();
        foreach (var id in ligados.Keys)
            variantesPorArticulo[id] = await _variantes!.ListarPorArticuloAsync(id, cancellationToken);

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
                ligados.ContainsKey(l.ArticuloId) ? variantesPorArticulo[l.ArticuloId] : null);
        }).ToList();
    }
}
