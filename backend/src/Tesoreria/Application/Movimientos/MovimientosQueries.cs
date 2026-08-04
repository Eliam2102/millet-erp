using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Movimientos;

// ============================================================================
// TES-PR2: libro de movimientos (§7.2 MovimientosBancariosQuery +
// MovimientoDetalleQuery). Los nombres de beneficiario (Proveedor/Cliente)
// se resuelven vía read-port de DatosMaestros cuando llegue en PR-3
// (ADR-0042); mientras tanto el response lleva solo los IDs.
// ============================================================================

public sealed record MovimientoBancarioResponse(
    Guid Id,
    Guid CuentaBancariaId,
    SentidoMovimiento Sentido,
    decimal Monto,
    string Moneda,
    DateOnly FechaValor,
    string? ReferenciaBancaria,
    Guid? ConceptoId,
    string? ConceptoNombre,
    EstadoAplicacionMovimiento EstadoAplicacion,
    EstadoConciliacionMovimiento EstadoConciliacion,
    BeneficiarioTipo? BeneficiarioTipo,
    Guid? BeneficiarioRef,
    Guid? ContramovimientoDe,
    string? MotivoNoAplicado,
    Guid CreadoPor,
    DateTimeOffset CreadoEn,
    int Version);

internal static class MovimientoBancarioMapper
{
    public static MovimientoBancarioResponse ToResponse(MovimientoBancario m, string? conceptoNombre = null) =>
        new(m.Id, m.CuentaBancariaId, m.Sentido, m.Monto, m.Moneda, m.FechaValor,
            m.ReferenciaBancaria, m.ConceptoId, conceptoNombre, m.EstadoAplicacion,
            m.EstadoConciliacion, m.BeneficiarioTipo, m.BeneficiarioRef,
            m.ContramovimientoDe, m.MotivoNoAplicado, m.CreadoPor, m.CreadoEn, m.Version);
}

// --------------------------------------------------- Libro (bandeja paginada)

public sealed record MovimientosBancariosQuery(
    Guid? CuentaBancariaId = null,
    SentidoMovimiento? Sentido = null,
    EstadoAplicacionMovimiento? EstadoAplicacion = null,
    EstadoConciliacionMovimiento? EstadoConciliacion = null,
    DateOnly? Desde = null,
    DateOnly? Hasta = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<MovimientoBancarioResponse>>;

public sealed class MovimientosBancariosHandler
    : IRequestHandler<MovimientosBancariosQuery, PagedResponse<MovimientoBancarioResponse>>
{
    private readonly TesoreriaDbContext _db;
    public MovimientosBancariosHandler(TesoreriaDbContext db) { _db = db; }

    public async Task<PagedResponse<MovimientoBancarioResponse>> Handle(
        MovimientosBancariosQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.MovimientosBancarios.AsNoTracking();
        if (query.CuentaBancariaId is Guid cuenta) q = q.Where(m => m.CuentaBancariaId == cuenta);
        if (query.Sentido is SentidoMovimiento s) q = q.Where(m => m.Sentido == s);
        if (query.EstadoAplicacion is EstadoAplicacionMovimiento ea) q = q.Where(m => m.EstadoAplicacion == ea);
        if (query.EstadoConciliacion is EstadoConciliacionMovimiento ec) q = q.Where(m => m.EstadoConciliacion == ec);
        if (query.Desde is DateOnly desde) q = q.Where(m => m.FechaValor >= desde);
        if (query.Hasta is DateOnly hasta) q = q.Where(m => m.FechaValor <= hasta);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(m => m.FechaValor).ThenByDescending(m => m.CreadoEn)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        // Nombre del concepto en dos pasos (catálogo chico, misma BD) en vez
        // de left join en la query — legible y sin sorpresas de traducción.
        var conceptoIds = items.Where(m => m.ConceptoId is not null)
            .Select(m => m.ConceptoId!.Value).Distinct().ToList();
        var conceptos = conceptoIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.ConceptosMovimiento.AsNoTracking()
                .Where(c => conceptoIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Nombre, cancellationToken);

        var responses = items
            .Select(m => MovimientoBancarioMapper.ToResponse(
                m, m.ConceptoId is Guid cid && conceptos.TryGetValue(cid, out var n) ? n : null))
            .ToList();

        return new PagedResponse<MovimientoBancarioResponse>(responses, offset, limit, total);
    }
}

// --------------------------------------------------- Detalle

/// <summary>Aplicación del movimiento a un pasivo — <c>PagoId</c> es el Id de la fila (correlación con CxP).</summary>
public sealed record AplicacionMovimientoDto(
    Guid PagoId,
    Guid FacturaProveedorId,
    Guid ProveedorId,
    decimal ImporteAplicado,
    bool Revertida,
    Guid? CorridaId,
    DateTimeOffset CreadoEn);

/// <summary>
/// Detalle del movimiento (TES-FE-PR2): el response base + sus
/// aplicaciones a pasivos (para revertir por <c>PagoId</c>) y los
/// contramovimientos que lo compensan (RN-10).
/// </summary>
public sealed record MovimientoDetalleResponse(
    MovimientoBancarioResponse Movimiento,
    IReadOnlyList<AplicacionMovimientoDto> Aplicaciones,
    IReadOnlyList<MovimientoBancarioResponse> Contramovimientos);

public sealed record MovimientoDetalleQuery(Guid Id) : IRequest<MovimientoDetalleResponse>;

public sealed class MovimientoDetalleHandler : IRequestHandler<MovimientoDetalleQuery, MovimientoDetalleResponse>
{
    private readonly TesoreriaDbContext _db;
    public MovimientoDetalleHandler(TesoreriaDbContext db) { _db = db; }

    public async Task<MovimientoDetalleResponse> Handle(
        MovimientoDetalleQuery query, CancellationToken cancellationToken)
    {
        var m = await _db.MovimientosBancarios.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException("MOV_NO_ENCONTRADO",
                $"No se encontró el movimiento bancario '{query.Id}'.");

        string? conceptoNombre = null;
        if (m.ConceptoId is Guid conceptoId)
        {
            conceptoNombre = await _db.ConceptosMovimiento.AsNoTracking()
                .Where(c => c.Id == conceptoId)
                .Select(c => c.Nombre)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var aplicaciones = await _db.AplicacionesPagoProveedor.AsNoTracking()
            .Where(a => a.MovimientoId == m.Id)
            .OrderBy(a => a.CreadoEn)
            .Select(a => new AplicacionMovimientoDto(
                a.Id, a.FacturaProveedorId, a.ProveedorId,
                a.ImporteAplicado, a.Revertida, a.CorridaId, a.CreadoEn))
            .ToListAsync(cancellationToken);

        var contramovimientos = await _db.MovimientosBancarios.AsNoTracking()
            .Where(x => x.ContramovimientoDe == m.Id)
            .OrderBy(x => x.CreadoEn)
            .ToListAsync(cancellationToken);

        return new MovimientoDetalleResponse(
            MovimientoBancarioMapper.ToResponse(m, conceptoNombre),
            aplicaciones,
            contramovimientos.Select(c => MovimientoBancarioMapper.ToResponse(c)).ToList());
    }
}
