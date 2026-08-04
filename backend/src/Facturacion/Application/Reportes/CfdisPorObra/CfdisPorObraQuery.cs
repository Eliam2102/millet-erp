using MediatR;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.Reportes.CfdisPorObra;

/// <summary>
/// Reporte CFDIs por Obra (§7.2, F11): los comprobantes vinculados a una obra,
/// vía <see cref="IFacturacionCfdiReadPort"/>. Contrato JSON ADR-0036.
/// </summary>
public sealed record CfdisPorObraQuery(long ObraId) : IRequest<ReporteResponse<CfdiObraFila>>;

public sealed record CfdiObraFila(
    Guid ComprobanteId,
    string Folio,
    string? Uuid,
    decimal Total,
    string Moneda,
    string Estado,
    DateTimeOffset? FechaTimbrado);

public sealed class CfdisPorObraHandler
    : IRequestHandler<CfdisPorObraQuery, ReporteResponse<CfdiObraFila>>
{
    private readonly IFacturacionCfdiReadPort _cfdis;
    private readonly IClock _clock;

    public CfdisPorObraHandler(IFacturacionCfdiReadPort cfdis, IClock clock)
    {
        _cfdis = cfdis;
        _clock = clock;
    }

    public async Task<ReporteResponse<CfdiObraFila>> Handle(CfdisPorObraQuery query, CancellationToken cancellationToken)
    {
        var cfdis = await _cfdis.ObtenerPorObraAsync(query.ObraId, cancellationToken);

        var filas = cfdis
            .Select(c => new CfdiObraFila(c.ComprobanteId, c.Folio, c.Uuid, c.Total, c.Moneda, c.Estado, c.FechaTimbrado))
            .ToList();

        return new ReporteResponse<CfdiObraFila>(
            Titulo: "CFDIs por Obra",
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados: new Dictionary<string, string?> { ["obraId"] = query.ObraId.ToString() },
            Columnas:
            [
                new("folio", "Folio", "texto"),
                new("uuid", "UUID", "texto"),
                new("total", "Total", "moneda", "derecha"),
                new("moneda", "Moneda", "texto"),
                new("estado", "Estado", "texto"),
                new("fechaTimbrado", "Timbrado", "fecha"),
            ],
            Filas: filas,
            Totales: new Dictionary<string, decimal> { ["total"] = filas.Sum(f => f.Total) });
    }
}
