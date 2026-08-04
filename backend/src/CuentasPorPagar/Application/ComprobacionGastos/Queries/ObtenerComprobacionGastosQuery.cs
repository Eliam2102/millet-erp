using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.ComprobacionGastos;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.ComprobacionGastos.Queries;

/// <summary>
/// Detalle de una comprobación de gastos (serie detalles CxP). Expone lo
/// que el list item omite: <b>las líneas (CFDIs) con su factura
/// generada</b>, pedimento (aduanales), observaciones y la trazabilidad
/// de revisión/autorización/aplicación/rechazo. Etiquetas de
/// sucursal/responsable resueltas server-side (ADR-0042).
/// </summary>
public sealed record ObtenerComprobacionGastosQuery(Guid Id) : IRequest<ComprobacionGastosDetalleResponse>;

public sealed record LineaComprobacionDetalleResponse(
    Guid Id,
    Guid FacturaProveedorId,
    Guid? CfdiRecibidoId,
    string? UuidCfdi,
    Guid ProveedorId,
    string? FolioProveedor,
    DateTimeOffset FechaCfdi,
    decimal Subtotal,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    decimal Total,
    string Moneda,
    string? Concepto);

public sealed record ComprobacionGastosDetalleResponse(
    Guid Id,
    TipoComprobacionGastos Tipo,
    Guid SucursalId,
    Guid ResponsableId,
    DateOnly FechaInicio,
    DateOnly FechaFin,
    string Moneda,
    decimal MontoTotal,
    EstadoComprobacionGastos Estado,
    string? NumeroPedimento,
    string? Observaciones,
    Guid? AutorizadoPorNivel1,
    DateTimeOffset? FechaAutorizacionNivel1,
    Guid? AutorizadoPor,
    DateTimeOffset? FechaAutorizacion,
    Guid? AplicadoPor,
    DateTimeOffset? FechaAplicacion,
    Guid? RechazadoPor,
    DateTimeOffset? FechaRechazo,
    string? MotivoRechazo,
    DateTimeOffset FechaCreacion,
    DateTimeOffset? FechaEnvioRevision,
    IReadOnlyList<LineaComprobacionDetalleResponse> Lineas,
    int Version,
    string? SucursalNombre = null,
    string? ResponsableNombre = null);

public sealed class ObtenerComprobacionGastosHandler
    : IRequestHandler<ObtenerComprobacionGastosQuery, ComprobacionGastosDetalleResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ISucursalReadPort _sucursales;
    private readonly IEmpleadoReadPort _empleados;

    public ObtenerComprobacionGastosHandler(
        CuentasPorPagarDbContext db, ISucursalReadPort sucursales, IEmpleadoReadPort empleados)
    {
        _db = db; _sucursales = sucursales; _empleados = empleados;
    }

    public async Task<ComprobacionGastosDetalleResponse> Handle(
        ObtenerComprobacionGastosQuery query, CancellationToken cancellationToken)
    {
        var c = await _db.ComprobacionesGastos
            .AsNoTracking()
            .Include(x => x.Lineas)
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "COMP_NO_ENCONTRADA", $"No se encontró la comprobación '{query.Id}'.");

        // Etiquetas best-effort — el responsable es empleado del catálogo
        // desde #630; capturas previas con usuario de Identidad resuelven null.
        var sucursal = await _sucursales.ObtenerAsync(c.SucursalId, cancellationToken);
        var responsable = await _empleados.ObtenerAsync(c.ResponsableId, cancellationToken);

        var lineas = c.Lineas
            .OrderBy(l => l.FechaCfdi)
            .Select(l => new LineaComprobacionDetalleResponse(
                l.Id, l.FacturaProveedorId, l.CfdiRecibidoId, l.UuidCfdi,
                l.ProveedorId, l.FolioProveedor, l.FechaCfdi,
                l.Subtotal, l.ImpuestosTrasladados, l.Retenciones, l.Total,
                l.Moneda, l.Concepto))
            .ToList();

        return new ComprobacionGastosDetalleResponse(
            c.Id, c.Tipo, c.SucursalId, c.ResponsableId, c.FechaInicio,
            c.FechaFin, c.Moneda, c.MontoTotal, c.Estado, c.NumeroPedimento,
            c.Observaciones,
            c.AutorizadoPorNivel1, c.FechaAutorizacionNivel1,
            c.AutorizadoPor, c.FechaAutorizacion,
            c.AplicadoPor, c.FechaAplicacion,
            c.RechazadoPor, c.FechaRechazo, c.MotivoRechazo,
            c.FechaCreacion, c.FechaEnvioRevision,
            lineas, c.Version,
            SucursalNombre: sucursal?.Nombre,
            ResponsableNombre: responsable?.Nombre);
    }
}
