using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Viaticos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Viaticos.Queries;

/// <summary>
/// Detalle de una solicitud de viáticos (serie detalles CxP). Expone lo
/// que el list item omite: política aplicada (puesto/destino/tope
/// snapshot), trazabilidad de firmas y fechas del ciclo, y las
/// <b>líneas de la comprobación</b> capturada. Etiquetas de
/// empleado/jefe/puesto resueltas server-side (ADR-0042).
/// </summary>
public sealed record ObtenerSolicitudViaticosQuery(Guid Id) : IRequest<SolicitudViaticosDetalleResponse>;

public sealed record LineaViaticosDetalleResponse(
    Guid Id,
    Guid? FacturaProveedorId,
    Guid? CfdiRecibidoId,
    string? UuidCfdi,
    Guid? ProveedorId,
    string? FolioProveedor,
    DateTimeOffset FechaGasto,
    decimal Subtotal,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    decimal Total,
    string Moneda,
    string Concepto,
    bool EsTicketNoFiscal);

public sealed record SolicitudViaticosDetalleResponse(
    Guid Id,
    Guid EmpleadoId,
    Guid PuestoId,
    Guid JefeDirectoId,
    string Destino,
    TipoDestinoViatico TipoDestino,
    DateOnly FechaSalida,
    DateOnly FechaRegreso,
    int DiasEstimados,
    string Moneda,
    decimal MontoSolicitado,
    decimal TopePolitica,
    bool ExcedePolitica,
    string? JustificacionExceso,
    EstadoSolicitudViaticos Estado,
    Guid? AutorizadoPorJefe,
    DateTimeOffset? FechaAutorizacionJefe,
    Guid? AutorizadoPorDf,
    DateTimeOffset? FechaAutorizacionDf,
    Guid? RechazadoPor,
    DateTimeOffset? FechaRechazo,
    string? MotivoRechazo,
    DateTimeOffset FechaSolicitud,
    DateTimeOffset? FechaAnticipoPagado,
    DateTimeOffset? FechaComprobacion,
    DateTimeOffset? FechaLiquidacion,
    decimal? MontoComprobado,
    decimal? DiferenciaLiquidacion,
    IReadOnlyList<LineaViaticosDetalleResponse> Lineas,
    int Version,
    string? EmpleadoNombre = null,
    string? JefeDirectoNombre = null,
    string? PuestoNombre = null);

public sealed class ObtenerSolicitudViaticosHandler
    : IRequestHandler<ObtenerSolicitudViaticosQuery, SolicitudViaticosDetalleResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IEmpleadoReadPort _empleados;
    private readonly IPuestoReadPort _puestos;

    public ObtenerSolicitudViaticosHandler(
        CuentasPorPagarDbContext db, IEmpleadoReadPort empleados, IPuestoReadPort puestos)
    {
        _db = db; _empleados = empleados; _puestos = puestos;
    }

    public async Task<SolicitudViaticosDetalleResponse> Handle(
        ObtenerSolicitudViaticosQuery query, CancellationToken cancellationToken)
    {
        var s = await _db.SolicitudesViaticos
            .AsNoTracking()
            .Include(x => x.Lineas)
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "VIA_NO_ENCONTRADA", $"No se encontró la solicitud de viáticos '{query.Id}'.");

        // Etiquetas best-effort — el detalle no se cae si un catálogo no resuelve.
        var empleado = await _empleados.ObtenerAsync(s.EmpleadoId, cancellationToken);
        var jefe = await _empleados.ObtenerAsync(s.JefeDirectoId, cancellationToken);
        var puesto = await _puestos.ObtenerAsync(s.PuestoId, cancellationToken);

        var lineas = s.Lineas
            .OrderBy(l => l.FechaGasto)
            .Select(l => new LineaViaticosDetalleResponse(
                l.Id, l.FacturaProveedorId, l.CfdiRecibidoId, l.UuidCfdi,
                l.ProveedorId, l.FolioProveedor, l.FechaGasto,
                l.Subtotal, l.ImpuestosTrasladados, l.Retenciones, l.Total,
                l.Moneda, l.Concepto, l.EsTicketNoFiscal))
            .ToList();

        return new SolicitudViaticosDetalleResponse(
            s.Id, s.EmpleadoId, s.PuestoId, s.JefeDirectoId, s.Destino,
            s.TipoDestino, s.FechaSalida, s.FechaRegreso, s.DiasEstimados,
            s.Moneda, s.MontoSolicitado, s.TopePoliticaSnapshot, s.ExcedePolitica,
            s.JustificacionExceso, s.Estado,
            s.AutorizadoPorJefe, s.FechaAutorizacionJefe,
            s.AutorizadoPorDf, s.FechaAutorizacionDf,
            s.RechazadoPor, s.FechaRechazo, s.MotivoRechazo,
            s.FechaSolicitud, s.FechaAnticipoPagado, s.FechaComprobacion,
            s.FechaLiquidacion, s.MontoComprobado, s.DiferenciaLiquidacion,
            lineas, s.Version,
            EmpleadoNombre: empleado?.Nombre,
            JefeDirectoNombre: jefe?.Nombre,
            PuestoNombre: puesto?.Nombre);
    }
}
