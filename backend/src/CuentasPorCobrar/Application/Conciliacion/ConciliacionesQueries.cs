using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Reportes.Comun;
using Millet.CuentasPorCobrar.Domain.Conciliacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.Conciliacion;

public sealed record ConciliacionResumenResponse(
    Guid Id,
    DateOnly Fecha,
    string Origen,
    Guid? EjecutadaPor,
    DateTimeOffset EjecutadaEn,
    int ComprobantesRevisados,
    int TotalDiferencias)
{
    public static ConciliacionResumenResponse De(ConciliacionCartera c) => new(
        c.Id, c.Fecha, c.Origen.ToString(), c.EjecutadaPor, c.EjecutadaEn, c.ComprobantesRevisados, c.TotalDiferencias);
}

/// <summary>Corridas de conciliación, la más reciente primero (C7.7).</summary>
public sealed record ListarConciliacionesQuery(DateOnly? Desde, DateOnly? Hasta, int Limite = 60)
    : IRequest<IReadOnlyList<ConciliacionResumenResponse>>;

public sealed class ListarConciliacionesHandler(CuentasPorCobrarDbContext db)
    : IRequestHandler<ListarConciliacionesQuery, IReadOnlyList<ConciliacionResumenResponse>>
{
    public async Task<IReadOnlyList<ConciliacionResumenResponse>> Handle(
        ListarConciliacionesQuery request, CancellationToken cancellationToken)
    {
        var q = db.ConciliacionesCartera.AsNoTracking();
        if (request.Desde is DateOnly desde) q = q.Where(c => c.Fecha >= desde);
        if (request.Hasta is DateOnly hasta) q = q.Where(c => c.Fecha <= hasta);
        var corridas = await q
            .OrderByDescending(c => c.EjecutadaEn).ThenByDescending(c => c.Fecha)
            .Take(Math.Clamp(request.Limite, 1, 366))
            .ToListAsync(cancellationToken);
        return corridas.Select(ConciliacionResumenResponse.De).ToList();
    }
}

/// <summary>Detalle de una corrida como reporte ADR-0036: una fila por diferencia.</summary>
public sealed record ConciliacionDetalleQuery(Guid Id) : IRequest<ConciliacionDetalleResponse>;

public sealed record ConciliacionDetalleResponse(ConciliacionResumenResponse Corrida, ReporteJsonResponse Reporte);

public sealed class ConciliacionDetalleHandler(CuentasPorCobrarDbContext db)
    : IRequestHandler<ConciliacionDetalleQuery, ConciliacionDetalleResponse>
{
    private static readonly IReadOnlyList<ColumnaReporte> Columnas =
    [
        new("tipo", "Diferencia", TipoColumnaReporte.Enum, AlineacionColumna.Izquierda),
        new("cliente", "Cliente", TipoColumnaReporte.Texto, AlineacionColumna.Izquierda),
        new("rfc", "RFC", TipoColumnaReporte.Texto, AlineacionColumna.Izquierda),
        new("comprobante", "Comprobante", TipoColumnaReporte.Texto, AlineacionColumna.Izquierda),
        new("folio", "Folio", TipoColumnaReporte.Texto, AlineacionColumna.Izquierda),
        new("importeFacturacion", "Importe en Facturación", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
        new("importeCartera", "Importe en cartera", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
        new("causa", "Causa probable", TipoColumnaReporte.Texto, AlineacionColumna.Izquierda),
    ];

    public static string Etiqueta(TipoDiferenciaConciliacion tipo) => tipo switch
    {
        TipoDiferenciaConciliacion.FacturaNoEnCartera => "Factura no está en cartera",
        TipoDiferenciaConciliacion.NotaCreditoNoReflejada => "NC no reflejada",
        TipoDiferenciaConciliacion.PagoNoReflejado => "Pago no reflejado",
        TipoDiferenciaConciliacion.CancelacionPendiente => "Cancelación pendiente",
        TipoDiferenciaConciliacion.ImporteDistinto => "Importe distinto",
        TipoDiferenciaConciliacion.CanceladoSoloEnCartera => "Cancelado solo en cartera",
        _ => tipo.ToString(),
    };

    public async Task<ConciliacionDetalleResponse> Handle(ConciliacionDetalleQuery request, CancellationToken cancellationToken)
    {
        var c = await db.ConciliacionesCartera.AsNoTracking()
            .Include(x => x.Diferencias)
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CC_NO_ENCONTRADA", $"No existe la conciliación {request.Id}.");

        var filas = c.Diferencias
            .OrderBy(d => d.ReceptorNombre).ThenBy(d => d.Tipo).ThenBy(d => d.Folio)
            .Select(d => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["tipo"] = Etiqueta(d.Tipo),
                ["cliente"] = d.ReceptorNombre,
                ["rfc"] = d.ReceptorRfc,
                ["comprobante"] = d.TipoComprobante,
                ["folio"] = d.Folio,
                ["importeFacturacion"] = d.ImporteFacturacion,
                ["importeCartera"] = d.ImporteCartera,
                ["causa"] = d.CausaProbable,
            })
            .ToList();

        var reporte = new ReporteJsonResponse(
            $"Conciliación de cartera del {c.Fecha:dd/MM/yyyy}",
            c.EjecutadaEn,
            [
                new("Día conciliado", c.Fecha.ToString("dd/MM/yyyy")),
                new("Origen", c.Origen == OrigenConciliacion.Programada ? "Programada" : "A demanda"),
                new("Comprobantes revisados", c.ComprobantesRevisados.ToString()),
            ],
            Columnas,
            filas,
            new Dictionary<string, object?> { ["tipo"] = "Diferencias", ["cliente"] = c.TotalDiferencias.ToString() });

        return new(ConciliacionResumenResponse.De(c), reporte);
    }
}
