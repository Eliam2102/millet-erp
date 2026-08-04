using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.Reportes.EstadosFacturasAnticipo;

/// <summary>
/// Reporte Estados de facturas de anticipo (§7.2, §13.3): cada CFDI de anticipo
/// con su saldo amortizable y estado. Contrato JSON ADR-0036.
/// </summary>
public sealed record EstadosFacturasAnticipoQuery(
    Guid? ClienteId,
    EstadoAnticipo? Estado) : IRequest<ReporteResponse<EstadoFacturaAnticipoFila>>;

public sealed record EstadoFacturaAnticipoFila(
    string Folio,
    string Cliente,
    string? Uuid,
    string TipoAnticipo,
    decimal MontoCobrado,
    decimal MontoAmortizado,
    decimal Saldo,
    string Estado,
    bool Timbrado);

public sealed class EstadosFacturasAnticipoHandler
    : IRequestHandler<EstadosFacturasAnticipoQuery, ReporteResponse<EstadoFacturaAnticipoFila>>
{
    private readonly FacturacionDbContext _db;
    private readonly IClock _clock;

    public EstadosFacturasAnticipoHandler(FacturacionDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ReporteResponse<EstadoFacturaAnticipoFila>> Handle(
        EstadosFacturasAnticipoQuery query, CancellationToken cancellationToken)
    {
        var q =
            from a in _db.Anticipos.AsNoTracking()
            join f in _db.FacturasAnticipo.AsNoTracking() on a.FacturaAnticipoId equals f.Id
            select new { a, f };

        if (query.ClienteId is { } clienteId) q = q.Where(x => x.a.ClienteId == clienteId);
        if (query.Estado is { } estado) q = q.Where(x => x.a.Estado == estado);

        var rows = await q
            .OrderByDescending(x => x.f.FolioNumero)
            .Select(x => new
            {
                x.f.Folio,
                Cliente = x.f.ReceptorNombre,
                x.f.Uuid,
                x.a.TipoAnticipo,
                x.a.MontoCobrado,
                x.a.MontoAmortizado,
                x.a.Saldo,
                x.a.Estado,
            })
            .ToListAsync(cancellationToken);

        var filas = rows
            .Select(r => new EstadoFacturaAnticipoFila(
                r.Folio, r.Cliente, r.Uuid, r.TipoAnticipo.ToString(),
                r.MontoCobrado, r.MontoAmortizado, r.Saldo, r.Estado.ToString(), r.Uuid is not null))
            .ToList();

        return new ReporteResponse<EstadoFacturaAnticipoFila>(
            Titulo: "Estados de facturas de anticipo",
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados: new Dictionary<string, string?>
            {
                ["clienteId"] = query.ClienteId?.ToString(),
                ["estado"] = query.Estado?.ToString(),
            },
            Columnas:
            [
                new("folio", "Folio", "texto"),
                new("cliente", "Cliente", "texto"),
                new("tipoAnticipo", "Tipo", "texto"),
                new("montoCobrado", "Cobrado", "moneda", "derecha"),
                new("montoAmortizado", "Amortizado", "moneda", "derecha"),
                new("saldo", "Saldo", "moneda", "derecha"),
                new("estado", "Estado", "texto"),
                new("timbrado", "Timbrado", "texto"),
            ],
            Filas: filas,
            Totales: new Dictionary<string, decimal>
            {
                ["montoCobrado"] = filas.Sum(f => f.MontoCobrado),
                ["montoAmortizado"] = filas.Sum(f => f.MontoAmortizado),
                ["saldo"] = filas.Sum(f => f.Saldo),
            });
    }
}
