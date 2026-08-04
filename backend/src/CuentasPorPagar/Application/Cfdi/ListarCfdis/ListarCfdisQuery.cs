using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.Cfdi.ListarCfdis;

/// <summary>
/// Bandeja paginada de CFDIs recibidos (F1-PR1). Soporta el caso
/// principal "CFDIs por capturar" filtrando por
/// <see cref="EstadoCfdiRecibido.PorProcesar"/> + filtros secundarios.
///
/// <para>
/// El global query filter de empresa (ADR-0011) restringe automáticamente
/// a los CFDIs de la empresa actual del JWT. Orden por
/// <c>FechaRecepcion DESC</c> para coincidir con el índice parcial
/// <c>ix_cfdis_recibidos_estado_fecha</c>.
/// </para>
/// </summary>
public sealed record ListarCfdisQuery(
    EstadoCfdiRecibido? Estado = null,
    TipoCfdi? Tipo = null,
    string? RfcEmisor = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<CfdiListItemResponse>>;

public sealed record CfdiListItemResponse(
    Guid Id,
    string UuidCfdi,
    string RfcEmisor,
    TipoCfdi Tipo,
    string? Folio,
    string? Serie,
    DateTimeOffset FechaCfdi,
    decimal Total,
    // Importes persistidos desde la ingesta; el FE los usa para
    // pre-llenar la captura de factura sin re-parsear el XML.
    decimal Subtotal,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    decimal? TipoCambio,
    string Moneda,
    CanalOrigenCfdi CanalOrigen,
    DateTimeOffset FechaRecepcion,
    EstadoCfdiRecibido Estado);

public sealed class ListarCfdisHandler : IRequestHandler<ListarCfdisQuery, PagedResponse<CfdiListItemResponse>>
{
    private readonly CuentasPorPagarDbContext _db;

    public ListarCfdisHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<PagedResponse<CfdiListItemResponse>> Handle(
        ListarCfdisQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.CfdisRecibidos.AsNoTracking();

        if (query.Estado is EstadoCfdiRecibido estado)
            q = q.Where(c => c.Estado == estado);
        if (query.Tipo is TipoCfdi tipo)
            q = q.Where(c => c.Tipo == tipo);
        if (!string.IsNullOrWhiteSpace(query.RfcEmisor))
        {
            var rfc = RfcMexicano.Parse(query.RfcEmisor);
            q = q.Where(c => c.RfcEmisor == rfc);
        }

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(c => c.FechaRecepcion)
            .Skip(offset)
            .Take(limit)
            .Select(c => new CfdiListItemResponse(
                c.Id,
                c.UuidCfdi.Valor,
                c.RfcEmisor.Valor,
                c.Tipo,
                c.Folio,
                c.Serie,
                c.FechaCfdi,
                c.Total,
                c.Subtotal,
                c.ImpuestosTrasladados,
                c.Retenciones,
                c.TipoCambio,
                c.Moneda,
                c.CanalOrigen,
                c.FechaRecepcion,
                c.Estado))
            .ToListAsync(cancellationToken);

        return new PagedResponse<CfdiListItemResponse>(items, offset, limit, total);
    }
}
