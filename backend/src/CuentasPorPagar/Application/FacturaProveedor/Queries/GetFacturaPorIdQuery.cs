using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.Queries;

public sealed record GetFacturaPorIdQuery(Guid Id) : IRequest<FacturaDetalleResponse>;

public sealed record FacturaDetalleResponse(
    Guid Id,
    Guid EmpresaId,
    Guid? CfdiRecibidoId,
    string? UuidCfdi,
    Guid ProveedorId,
    Guid SucursalId,
    string? FolioProveedor,
    string? SerieProveedor,
    DateTimeOffset FechaDocumento,
    DateTimeOffset FechaContabilizacion,
    DateOnly FechaVencimiento,
    string Moneda,
    decimal? TipoCambio,
    decimal Subtotal,
    decimal Descuentos,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    decimal Total,
    Guid? OrdenCompraId,
    EstadoPasivo Estado,
    decimal DiferenciaContraOc,
    decimal AnticipoAplicadoTotal,
    decimal NcAplicadasTotal,
    decimal ImportePagado,
    decimal SaldoPendiente,
    MotivoCancelacion? MotivoCancelacion,
    string? MotivoCancelacionTexto,
    bool EnRevision,
    int Version,
    IReadOnlyList<FacturaLineaResponse> Lineas,
    // Etiquetas resueltas server-side vía read ports (ADR-0042): el
    // cliente no tiene catálogo completo. Null si el id no resuelve.
    string? ProveedorNombre = null,
    string? SucursalNombre = null);

public sealed record FacturaLineaResponse(
    Guid Id,
    int Posicion,
    Guid? ArticuloId,
    string? ClaveProdServ,
    string Descripcion,
    decimal Cantidad,
    string ClaveUnidad,
    decimal PrecioUnitario,
    decimal Importe,
    decimal? Descuento,
    Guid? LineaOcId,
    Guid? ConceptoContableId);

public sealed class GetFacturaPorIdHandler : IRequestHandler<GetFacturaPorIdQuery, FacturaDetalleResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IProveedorReadPort _proveedores;
    private readonly ISucursalReadPort _sucursales;

    public GetFacturaPorIdHandler(
        CuentasPorPagarDbContext db,
        IProveedorReadPort proveedores,
        ISucursalReadPort sucursales)
    {
        _db = db;
        _proveedores = proveedores;
        _sucursales = sucursales;
    }

    public async Task<FacturaDetalleResponse> Handle(GetFacturaPorIdQuery query, CancellationToken cancellationToken)
    {
        var f = await _db.FacturasProveedor
            .AsNoTracking()
            .Include(f => f.Lineas)
            .FirstOrDefaultAsync(f => f.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "FACTURA_NO_ENCONTRADA",
                $"No se encontró la factura con id '{query.Id}'.");

        var proveedor = await _proveedores.ObtenerAsync(f.ProveedorId, cancellationToken);
        var sucursal = await _sucursales.ObtenerAsync(f.SucursalId, cancellationToken);

        return new FacturaDetalleResponse(
            Id: f.Id,
            EmpresaId: f.EmpresaId,
            CfdiRecibidoId: f.CfdiRecibidoId,
            UuidCfdi: f.UuidCfdi,
            ProveedorId: f.ProveedorId,
            SucursalId: f.SucursalId,
            FolioProveedor: f.FolioProveedor,
            SerieProveedor: f.SerieProveedor,
            FechaDocumento: f.FechaDocumento,
            FechaContabilizacion: f.FechaContabilizacion,
            FechaVencimiento: f.FechaVencimiento,
            Moneda: f.Moneda,
            TipoCambio: f.TipoCambio,
            Subtotal: f.Subtotal,
            Descuentos: f.Descuentos,
            ImpuestosTrasladados: f.ImpuestosTrasladados,
            Retenciones: f.Retenciones,
            Total: f.Total,
            OrdenCompraId: f.OrdenCompraId,
            Estado: f.Estado,
            DiferenciaContraOc: f.DiferenciaContraOc,
            AnticipoAplicadoTotal: f.AnticipoAplicadoTotal,
            NcAplicadasTotal: f.NcAplicadasTotal,
            ImportePagado: f.ImportePagado,
            SaldoPendiente: f.SaldoPendiente,
            MotivoCancelacion: f.MotivoDeCancelacion,
            MotivoCancelacionTexto: f.MotivoCancelacionTexto,
            EnRevision: f.EnRevision,
            Version: f.Version,
            Lineas: f.Lineas.OrderBy(l => l.Posicion).Select(l => new FacturaLineaResponse(
                l.Id, l.Posicion, l.ArticuloId, l.ClaveProdServ, l.Descripcion,
                l.Cantidad, l.ClaveUnidad, l.PrecioUnitario, l.Importe, l.Descuento,
                l.LineaOcId, l.ConceptoContableId)).ToList(),
            ProveedorNombre: proveedor?.RazonSocial,
            SucursalNombre: sucursal is null ? null : $"{sucursal.Codigo} · {sucursal.Nombre}");
    }
}
