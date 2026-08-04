using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Facturas.Queries;

/// <summary>
/// Detalle de una factura de venta + sus líneas + datos del timbre + la cadena de
/// relaciones CFDI (§7.2 diseño; B4, FE-F2). Cada relación se enriquece con el
/// folio/tipo/total del comprobante relacionado, resuelto por UUID.
/// </summary>
public sealed record ComprobanteDetalleQuery(Guid Id) : IRequest<ComprobanteDetalleResponse>;

public sealed record ComprobanteDetalleResponse(
    Guid Id,
    string Tipo,
    string Folio,
    string Estado,
    string? Uuid,
    string ReceptorRfc,
    string ReceptorNombre,
    string Moneda,
    decimal Subtotal,
    decimal Descuento,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    decimal Total,
    DateTimeOffset? FechaTimbrado,
    int Version,
    IReadOnlyList<ComprobanteLineaDetalle> Lineas,
    IReadOnlyList<RelacionCfdiDetalle> Relaciones,
    // Error del último intento de timbrado (TimbradoFallido) — habilita el
    // banner + botón de reintento en el detalle (PR-B).
    string? TimbradoErrorCodigo = null,
    string? TimbradoErrorMensaje = null,
    // Folio que asignó el PAC al CFDI (consecutivo por RFC emisor).
    string? FolioPac = null,
    // [Decisión 13-K]: NC timbradas que acreditan a esta factura (amortización
    // de anticipos relación 07 y NC generales) y el monto que queda por cobrar.
    decimal TotalAcreditado = 0m,
    decimal TotalPorCobrar = 0m,
    IReadOnlyList<NotaCreditoAplicadaDetalle>? NotasCreditoAplicadas = null);

/// <summary>NC timbrada que acredita a la factura ([Decisión 13-K]).</summary>
public sealed record NotaCreditoAplicadaDetalle(
    Guid Id,
    string Folio,
    string Motivo,
    decimal Total,
    string? Uuid,
    DateTimeOffset? FechaTimbrado);

public sealed record ComprobanteLineaDetalle(
    int Posicion,
    string ClaveProdServSat,
    string Descripcion,
    string ClaveUnidadSat,
    decimal Cantidad,
    decimal ValorUnitario,
    decimal Descuento,
    decimal Importe);

public sealed record RelacionCfdiDetalle(
    string TipoRelacion,
    string UuidRelacionado,
    string? Folio,
    string? TipoComprobante,
    decimal? Total,
    DateTimeOffset? FechaTimbrado);

public sealed class ComprobanteDetalleHandler
    : IRequestHandler<ComprobanteDetalleQuery, ComprobanteDetalleResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;

    public ComprobanteDetalleHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance)
    {
        _db = db;
        _alcance = alcance;
    }

    public async Task<ComprobanteDetalleResponse> Handle(
        ComprobanteDetalleQuery query,
        CancellationToken cancellationToken)
    {
        // Fuera de alcance → mismo 404 que inexistente (12-cajas.md §4.1).
        var alcance = await _alcance.ResolverAsync(cancellationToken);
        var f = await alcance.AplicarA(_db.FacturasVenta.AsNoTracking())
            .Include(x => x.Lineas)
            .Include(x => x.Relaciones)
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", $"No existe la factura '{query.Id}'.");

        var lineas = f.Lineas
            .OrderBy(l => l.Posicion)
            .Select(l => new ComprobanteLineaDetalle(
                l.Posicion, l.ClaveProdServSat, l.Descripcion, l.ClaveUnidadSat,
                l.Cantidad, l.ValorUnitario, l.Descuento, l.Importe))
            .ToList();

        // Enriquecer las relaciones con el comprobante relacionado (por UUID).
        var uuids = f.Relaciones.Select(r => r.UuidRelacionado).Distinct().ToList();
        var relacionados = uuids.Count == 0
            ? []
            : await _db.Comprobantes.AsNoTracking()
                .Where(c => c.Uuid != null && uuids.Contains(c.Uuid))
                .Select(c => new { c.Uuid, c.Folio, c.Tipo, c.Total, c.FechaTimbrado })
                .ToListAsync(cancellationToken);
        var porUuid = relacionados.ToDictionary(c => c.Uuid!, c => c);

        var relaciones = f.Relaciones
            .Select(r =>
            {
                porUuid.TryGetValue(r.UuidRelacionado, out var c);
                return new RelacionCfdiDetalle(
                    r.TipoRelacion, r.UuidRelacionado, c?.Folio, c?.Tipo.ToString(), c?.Total, c?.FechaTimbrado);
            })
            .ToList();

        // [Decisión 13-K]: NC timbradas que acreditan a la factura → monto por cobrar.
        var ncAplicadas = (await _db.NotasCredito.AsNoTracking()
                .Where(n => n.FacturaRelacionadaId == f.Id && n.Estado == EstadoTimbrado.Timbrado)
                .OrderBy(n => n.FolioNumero)
                .Select(n => new { n.Id, n.Folio, n.Motivo, n.Total, n.Uuid, n.FechaTimbrado })
                .ToListAsync(cancellationToken))
            .Select(n => new NotaCreditoAplicadaDetalle(
                n.Id, n.Folio, n.Motivo.ToString(), n.Total, n.Uuid, n.FechaTimbrado))
            .ToList();
        var totalAcreditado = ncAplicadas.Sum(n => n.Total);

        return new ComprobanteDetalleResponse(
            f.Id, f.Tipo.ToString(), f.Folio, f.Estado.ToString(), f.Uuid,
            f.ReceptorRfc, f.ReceptorNombre, f.Moneda,
            f.Subtotal, f.Descuento, f.ImpuestosTrasladados, f.Retenciones, f.Total,
            f.FechaTimbrado, f.Version, lineas, relaciones,
            f.TimbradoErrorCodigo, f.TimbradoErrorMensaje, f.FolioPac,
            TotalAcreditado: totalAcreditado,
            TotalPorCobrar: f.Total - totalAcreditado,
            NotasCreditoAplicadas: ncAplicadas);
    }
}
