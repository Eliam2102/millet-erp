using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.Reportes.ControlAnticipos;

/// <summary>
/// Reporte "Control de Anticipos" — vista Resumen (§6.7 levantamiento). Lista los
/// anticipos con su saldo amortizable, filtrable por cliente / estado / obra /
/// rango de fechas de emisión. Devuelve el contrato JSON ADR-0036
/// (<see cref="ReporteResponse{TFila}"/>) con totales de cobrado, amortizado y
/// saldo. Export PDF/Excel client-side.
/// </summary>
public sealed record ControlAnticiposResumenQuery(
    Guid? ClienteId,
    EstadoAnticipo? Estado,
    long? ObraId,
    DateTimeOffset? Desde,
    DateTimeOffset? Hasta) : IRequest<ReporteResponse<ControlAnticipoFila>>;

public sealed record ControlAnticipoFila(
    Guid AnticipoId,
    string Folio,
    string Cliente,
    string? Obra,
    string TipoAnticipo,
    string Moneda,
    decimal MontoCobrado,
    decimal MontoAmortizado,
    decimal Saldo,
    string Estado,
    string? PedidoOrigenRef,
    DateTimeOffset? FechaEmision,
    // 13-A/13-J: enlace al detalle de la factura de anticipo + estado del CFDI
    // (el picker excluye anticipos cuyo CFDI no está Timbrado).
    Guid FacturaAnticipoId,
    string EstadoCfdi);

public sealed class ControlAnticiposResumenHandler
    : IRequestHandler<ControlAnticiposResumenQuery, ReporteResponse<ControlAnticipoFila>>
{
    private readonly FacturacionDbContext _db;
    private readonly IClock _clock;

    public ControlAnticiposResumenHandler(FacturacionDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ReporteResponse<ControlAnticipoFila>> Handle(
        ControlAnticiposResumenQuery query,
        CancellationToken cancellationToken)
    {
        // Join anticipo ↔ factura_anticipo (folio, nombre receptor, fecha de timbre).
        var q =
            from a in _db.Anticipos.AsNoTracking()
            join f in _db.FacturasAnticipo.AsNoTracking() on a.FacturaAnticipoId equals f.Id
            select new { a, f };

        if (query.ClienteId is { } clienteId) q = q.Where(x => x.a.ClienteId == clienteId);
        if (query.Estado is { } estado) q = q.Where(x => x.a.Estado == estado);
        if (query.ObraId is { } obraId) q = q.Where(x => x.a.ObraId == obraId);
        if (query.Desde is { } desde) q = q.Where(x => x.f.FechaTimbrado >= desde);
        if (query.Hasta is { } hasta) q = q.Where(x => x.f.FechaTimbrado <= hasta);

        var rows = await q
            .OrderByDescending(x => x.f.FolioNumero)
            .Select(x => new
            {
                x.a.Id,
                x.f.Folio,
                Cliente = x.f.ReceptorNombre,
                x.a.ObraNombre,
                x.a.TipoAnticipo,
                x.a.Moneda,
                x.a.MontoCobrado,
                x.a.MontoAmortizado,
                x.a.Saldo,
                x.a.Estado,
                x.a.PedidoOrigenRef,
                x.f.FechaTimbrado,
                FacturaAnticipoId = x.f.Id,
                EstadoCfdi = x.f.Estado,
            })
            .ToListAsync(cancellationToken);

        var filas = rows
            .Select(r => new ControlAnticipoFila(
                r.Id, r.Folio, r.Cliente, r.ObraNombre,
                r.TipoAnticipo.ToString(), r.Moneda,
                r.MontoCobrado, r.MontoAmortizado, r.Saldo,
                r.Estado.ToString(), r.PedidoOrigenRef, r.FechaTimbrado,
                r.FacturaAnticipoId, r.EstadoCfdi.ToString()))
            .ToList();

        var totales = new Dictionary<string, decimal>
        {
            ["montoCobrado"] = filas.Sum(f => f.MontoCobrado),
            ["montoAmortizado"] = filas.Sum(f => f.MontoAmortizado),
            ["saldo"] = filas.Sum(f => f.Saldo),
        };

        var filtros = new Dictionary<string, string?>
        {
            ["clienteId"] = query.ClienteId?.ToString(),
            ["estado"] = query.Estado?.ToString(),
            ["obraId"] = query.ObraId?.ToString(),
            ["desde"] = query.Desde?.ToString("o"),
            ["hasta"] = query.Hasta?.ToString("o"),
        };

        var columnas = new List<ColumnaDescriptor>
        {
            new("folio", "Folio", "texto"),
            new("cliente", "Cliente", "texto"),
            new("obra", "Obra", "texto"),
            new("tipoAnticipo", "Tipo", "texto"),
            new("moneda", "Moneda", "texto"),
            new("montoCobrado", "Cobrado", "moneda", "derecha"),
            new("montoAmortizado", "Amortizado", "moneda", "derecha"),
            new("saldo", "Saldo", "moneda", "derecha"),
            new("estado", "Estado", "texto"),
            new("pedidoOrigenRef", "Pedido A+W", "texto"),
            new("fechaEmision", "Emisión", "fecha"),
        };

        return new ReporteResponse<ControlAnticipoFila>(
            Titulo: "Control de Anticipos",
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados: filtros,
            Columnas: columnas,
            Filas: filas,
            Totales: totales);
    }
}
