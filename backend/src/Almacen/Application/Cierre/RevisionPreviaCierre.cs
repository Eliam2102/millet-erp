using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Cierre;

/// <summary>
/// Conteos de pendientes que bloquean el cierre de un mes. Fuente única para el
/// comando de cierre y para la revisión previa (solo lectura).
/// </summary>
public sealed record PendientesCierre(
    IReadOnlyList<Guid> ConteosPendientes,
    int ValesPendientes,
    int MovimientosPendientes);

public static class RevisionPreviaCierre
{
    public static async Task<PendientesCierre> EvaluarPendientesAsync(
        AlmacenDbContext db, int anio, int mes, CancellationToken ct)
    {
        var inicioMes = new DateOnly(anio, mes, 1);
        var finMes = inicioMes.AddMonths(1).AddDays(-1);
        var finMesUtc = new DateTimeOffset(inicioMes.AddMonths(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var conteos = await db.Set<ConteoInventario>().AsNoTracking()
            .Where(c => (c.FechaPlanificada <= finMes || c.FechaInicio < finMesUtc)
                && c.Estado != EstadoConteo.Aplicado
                && c.Estado != EstadoConteo.Rechazado)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var vales = await db.Movimientos.AsNoTracking().CountAsync(m =>
            m.FechaMovimiento >= inicioMes && m.FechaMovimiento <= finMes
            && m.Tipo == TipoMovimiento.SalidaPorVale && m.PendienteRegularizacion
            && m.Estado == EstadoMovimiento.Registrado, ct);

        var movimientos = await db.Movimientos.AsNoTracking()
            .CountAsync(m => m.FechaMovimiento >= inicioMes
                && m.FechaMovimiento <= finMes
                && (m.Estado == EstadoMovimiento.Borrador
                    || m.Estado == EstadoMovimiento.Validado), ct);

        return new PendientesCierre(conteos, vales, movimientos);
    }
}

public sealed record PeriodoCierreItem(int Anio, int Mes, bool Cerrado, DateTimeOffset? CerradoAt);

/// <summary>
/// Periodos mensuales concluidos (últimos 12) más cualquier periodo ya cerrado, con su estado.
/// </summary>
public sealed record ListarPeriodosCierreQuery : IRequest<IReadOnlyList<PeriodoCierreItem>>;

public sealed class ListarPeriodosCierreHandler
    : IRequestHandler<ListarPeriodosCierreQuery, IReadOnlyList<PeriodoCierreItem>>
{
    private readonly AlmacenDbContext _db;
    public ListarPeriodosCierreHandler(AlmacenDbContext db) => _db = db;

    public async Task<IReadOnlyList<PeriodoCierreItem>> Handle(
        ListarPeriodosCierreQuery request, CancellationToken cancellationToken)
    {
        var cerrados = await _db.Set<PeriodoCerrado>().AsNoTracking()
            .Select(p => new { p.Anio, p.Mes, p.CerradoAt })
            .ToListAsync(cancellationToken);

        var hoy = FechaNegocio.Hoy(DateTimeOffset.UtcNow);
        var primeroDelMes = new DateOnly(hoy.Year, hoy.Month, 1);
        var periodos = new SortedDictionary<(int Anio, int Mes), DateTimeOffset?>(
            Comparer<(int Anio, int Mes)>.Create((a, b) => b.CompareTo(a)));
        for (var i = 1; i <= 12; i++)
        {
            var d = primeroDelMes.AddMonths(-i);
            periodos[(d.Year, d.Month)] = null;
        }
        foreach (var c in cerrados) periodos[(c.Anio, c.Mes)] = c.CerradoAt;

        return periodos
            .Select(kv => new PeriodoCierreItem(kv.Key.Anio, kv.Key.Mes, kv.Value is not null, kv.Value))
            .ToList();
    }
}

/// <param name="Codigo">Mismo código que lanza el cierre al fallar (CIERRE_CONTEOS_PENDIENTES, ...).</param>
public sealed record RevisionPreviaCheck(string Codigo, string Titulo, bool Ok, int Cantidad, string Detalle);

public sealed record RevisionPreviaCierreResponse(
    int Anio,
    int Mes,
    bool YaCerrado,
    bool PeriodoNoConcluido,
    bool PuedeCerrar,
    IReadOnlyList<RevisionPreviaCheck> Checks);

public sealed record ObtenerRevisionPreviaCierreQuery(int Anio, int Mes) : IRequest<RevisionPreviaCierreResponse>;

public sealed class ObtenerRevisionPreviaCierreHandler
    : IRequestHandler<ObtenerRevisionPreviaCierreQuery, RevisionPreviaCierreResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public ObtenerRevisionPreviaCierreHandler(AlmacenDbContext db, ICurrentEmpresaContext currentEmpresa)
    {
        _db = db; _currentEmpresa = currentEmpresa;
    }

    public async Task<RevisionPreviaCierreResponse> Handle(
        ObtenerRevisionPreviaCierreQuery request, CancellationToken cancellationToken)
    {
        if (request.Anio is < 2020 or > 2099 || request.Mes is < 1 or > 12)
            throw new BusinessRuleException("CIERRE_PERIODO_INVALIDO", "Periodo fuera de rango.");
        var empresaId = _currentEmpresa.Current ?? throw new BusinessRuleException(
            "CIERRE_SIN_EMPRESA", "Contexto de empresa requerido.");

        var hoy = FechaNegocio.Hoy(DateTimeOffset.UtcNow);
        var noConcluido = request.Anio > hoy.Year || (request.Anio == hoy.Year && request.Mes >= hoy.Month);
        var yaCerrado = await _db.Set<PeriodoCerrado>().AsNoTracking()
            .AnyAsync(p => p.EmpresaId == empresaId && p.Anio == request.Anio && p.Mes == request.Mes, cancellationToken);

        var p = await RevisionPreviaCierre.EvaluarPendientesAsync(_db, request.Anio, request.Mes, cancellationToken);
        var checks = new List<RevisionPreviaCheck>
        {
            new("CIERRE_CONTEOS_PENDIENTES", "Conteos del periodo aplicados o rechazados",
                p.ConteosPendientes.Count == 0, p.ConteosPendientes.Count,
                p.ConteosPendientes.Count == 0
                    ? "Sin conteos pendientes."
                    : $"{p.ConteosPendientes.Count} conteo(s) sin aplicar ni rechazar."),
            new("CIERRE_VALES_PENDIENTES", "Vales urgentes regularizados con requisición",
                p.ValesPendientes == 0, p.ValesPendientes,
                p.ValesPendientes == 0
                    ? "Sin vales pendientes de regularizar."
                    : $"{p.ValesPendientes} vale(s) pendientes de regularizar."),
            new("CIERRE_MOVIMIENTOS_PENDIENTES", "Movimientos firmados o cancelados",
                p.MovimientosPendientes == 0, p.MovimientosPendientes,
                p.MovimientosPendientes == 0
                    ? "Sin movimientos en Borrador o Validado."
                    : $"{p.MovimientosPendientes} movimiento(s) en Borrador o Validado."),
        };

        var puede = !yaCerrado && !noConcluido && checks.All(c => c.Ok);
        return new RevisionPreviaCierreResponse(
            request.Anio, request.Mes, yaCerrado, noConcluido, puede, checks);
    }
}
