using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.NotaCargo.Queries;

/// <summary>
/// Detalle de una nota de cargo (serie detalles CxP). Expone lo que el
/// list item omite: la devolución 8.B que la originó, la NC del
/// proveedor que la concilió y la trazabilidad completa
/// (creación/autorización/aplicación/formalización/cancelación).
/// </summary>
public sealed record ObtenerNotaCargoQuery(Guid Id) : IRequest<NotaCargoDetalleResponse>;

public sealed record NotaCargoDetalleResponse(
    Guid Id,
    string Folio,
    short FolioAnio,
    Guid ProveedorId,
    Guid? SucursalId,
    string Concepto,
    Guid? ConceptoContableId,
    decimal Monto,
    string Moneda,
    decimal? TipoCambio,
    Guid? FacturaOrigenId,
    Guid? DevolucionAProveedorId,
    Guid? NotaCreditoProveedorId,
    EstadoNotaCargo Estado,
    Guid? CreadoPor,
    Guid? AutorizadoPor,
    Guid? AplicadoPor,
    DateTimeOffset FechaCreacion,
    DateTimeOffset? FechaAutorizacion,
    DateTimeOffset? FechaAplicacion,
    DateTimeOffset? FechaFormalizacion,
    DateTimeOffset? FechaCancelacion,
    string? MotivoCancelacion,
    int Version,
    string? ProveedorNombre = null);

public sealed class ObtenerNotaCargoHandler
    : IRequestHandler<ObtenerNotaCargoQuery, NotaCargoDetalleResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IProveedorReadPort _proveedores;

    public ObtenerNotaCargoHandler(CuentasPorPagarDbContext db, IProveedorReadPort proveedores)
    {
        _db = db; _proveedores = proveedores;
    }

    public async Task<NotaCargoDetalleResponse> Handle(
        ObtenerNotaCargoQuery query, CancellationToken cancellationToken)
    {
        var nc = await _db.NotasCargo
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "NOTA_CARGO_NO_ENCONTRADA", $"No se encontró la nota de cargo '{query.Id}'.");

        var proveedor = await _proveedores.ObtenerAsync(nc.ProveedorId, cancellationToken);

        return new NotaCargoDetalleResponse(
            nc.Id, nc.Folio.Valor, nc.FolioAnio, nc.ProveedorId, nc.SucursalId,
            nc.Concepto, nc.ConceptoContableId, nc.Monto, nc.Moneda, nc.TipoCambio,
            nc.FacturaOrigenId, nc.DevolucionAProveedorId, nc.NotaCreditoProveedorId,
            nc.Estado, nc.CreadoPor, nc.AutorizadoPor, nc.AplicadoPor,
            nc.FechaCreacion, nc.FechaAutorizacion, nc.FechaAplicacion,
            nc.FechaFormalizacion, nc.FechaCancelacion, nc.MotivoCancelacion,
            nc.Version,
            ProveedorNombre: proveedor?.RazonSocial);
    }
}
