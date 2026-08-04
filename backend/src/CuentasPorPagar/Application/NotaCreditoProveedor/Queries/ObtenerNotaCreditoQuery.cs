using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.NotaCreditoProveedor.Queries;

/// <summary>
/// Detalle de una NC del proveedor (serie detalles CxP). Expone lo que
/// el list item omite: importes desglosados, vínculo con CfdiRecibido,
/// monto aplicado y trazabilidad de captura/match/cancelación. Etiqueta
/// <c>ProveedorNombre</c> resuelta server-side (ADR-0042).
/// </summary>
public sealed record ObtenerNotaCreditoQuery(Guid Id) : IRequest<NotaCreditoDetalleResponse>;

public sealed record NotaCreditoDetalleResponse(
    Guid Id,
    Guid? CfdiRecibidoId,
    string UuidCfdi,
    Guid ProveedorId,
    string? FolioProveedor,
    string? SerieProveedor,
    DateTimeOffset FechaCfdi,
    string Moneda,
    decimal? TipoCambio,
    decimal Subtotal,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    decimal Total,
    TipoNotaCredito Tipo,
    TipoRelacionCfdi TipoRelacionCfdi,
    string UuidRelacionCfdi,
    Guid? FacturaOrigenId,
    decimal MontoAplicado,
    decimal SaldoPorAplicar,
    EstadoNotaCredito Estado,
    DateTimeOffset FechaCaptura,
    DateTimeOffset? FechaMatch,
    DateTimeOffset? FechaCancelacion,
    string? MotivoCancelacion,
    Guid? CapturadoPor,
    int Version,
    string? ProveedorNombre = null);

public sealed class ObtenerNotaCreditoHandler
    : IRequestHandler<ObtenerNotaCreditoQuery, NotaCreditoDetalleResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IProveedorReadPort _proveedores;

    public ObtenerNotaCreditoHandler(CuentasPorPagarDbContext db, IProveedorReadPort proveedores)
    {
        _db = db; _proveedores = proveedores;
    }

    public async Task<NotaCreditoDetalleResponse> Handle(
        ObtenerNotaCreditoQuery query, CancellationToken cancellationToken)
    {
        var nc = await _db.NotasCreditoProveedor
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "NC_NO_ENCONTRADA", $"No se encontró la nota de crédito '{query.Id}'.");

        var proveedor = await _proveedores.ObtenerAsync(nc.ProveedorId, cancellationToken);

        return new NotaCreditoDetalleResponse(
            nc.Id, nc.CfdiRecibidoId, nc.UuidCfdi, nc.ProveedorId,
            nc.FolioProveedor, nc.SerieProveedor, nc.FechaCfdi, nc.Moneda,
            nc.TipoCambio, nc.Subtotal, nc.ImpuestosTrasladados, nc.Retenciones,
            nc.Total, nc.Tipo, nc.TipoRelacionCfdi, nc.UuidRelacionCfdi,
            nc.FacturaOrigenId, nc.MontoAplicado, nc.SaldoPorAplicar, nc.Estado,
            nc.FechaCaptura, nc.FechaMatch, nc.FechaCancelacion,
            nc.MotivoCancelacion, nc.CapturadoPor, nc.Version,
            ProveedorNombre: proveedor?.RazonSocial);
    }
}
