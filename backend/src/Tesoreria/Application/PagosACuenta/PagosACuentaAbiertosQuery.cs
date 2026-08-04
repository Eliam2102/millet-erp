using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.PagosACuenta;

// ============================================================================
// TES-PR6: read model de pagos a cuenta abiertos con antigüedad (§3.4
// paso 4). Reemplaza el reporte semanal manual "pagos efectuados no
// reconciliados" y da a CxP la lista exacta de provisiones pendientes.
// Incluye los parcialmente ligados (flag por estado).
// ============================================================================

public sealed record PagoACuentaAbiertoResponse(
    Guid MovimientoId,
    Guid CuentaBancariaId,
    Guid? ProveedorId,
    string? ProveedorClave,
    string? ProveedorRazonSocial,
    decimal Monto,
    decimal ImporteLigado,
    string Moneda,
    DateOnly FechaValor,
    string? ReferenciaBancaria,
    string Motivo,
    EstadoAplicacionMovimiento EstadoAplicacion,
    int AntiguedadDias);

public sealed record PagosACuentaAbiertosQuery(
    Guid? ProveedorId = null,
    bool IncluirParciales = true,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<PagoACuentaAbiertoResponse>>;

public sealed class PagosACuentaAbiertosHandler
    : IRequestHandler<PagosACuentaAbiertosQuery, PagedResponse<PagoACuentaAbiertoResponse>>
{
    private readonly TesoreriaDbContext _db;
    private readonly IProveedorBancoReadPort _proveedores;
    private readonly IClock _clock;

    public PagosACuentaAbiertosHandler(
        TesoreriaDbContext db,
        IProveedorBancoReadPort proveedores,
        IClock clock)
    {
        _db = db; _proveedores = proveedores; _clock = clock;
    }

    public async Task<PagedResponse<PagoACuentaAbiertoResponse>> Handle(
        PagosACuentaAbiertosQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        // Pago a cuenta = egreso con motivo_no_aplicado, no contramovimiento.
        var q = _db.MovimientosBancarios.AsNoTracking()
            .Where(m => m.Sentido == SentidoMovimiento.Egreso
                        && m.MotivoNoAplicado != null
                        && m.ContramovimientoDe == null);

        q = query.IncluirParciales
            ? q.Where(m => m.EstadoAplicacion != EstadoAplicacionMovimiento.Aplicado)
            : q.Where(m => m.EstadoAplicacion == EstadoAplicacionMovimiento.NoAplicado);

        if (query.ProveedorId is Guid proveedor)
            q = q.Where(m => m.BeneficiarioRef == proveedor);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(m => m.FechaValor)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        var movIds = items.Select(m => m.Id).ToList();
        var ligados = movIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _db.AplicacionesPagoProveedor.AsNoTracking()
                .Where(a => movIds.Contains(a.MovimientoId) && !a.Revertida)
                .GroupBy(a => a.MovimientoId)
                .Select(g => new { g.Key, Suma = g.Sum(a => a.ImporteAplicado) })
                .ToDictionaryAsync(x => x.Key, x => x.Suma, cancellationToken);

        var proveedores = await _proveedores.ObtenerVariosAsync(
            items.Where(m => m.BeneficiarioRef is not null)
                .Select(m => m.BeneficiarioRef!.Value).Distinct().ToList(),
            cancellationToken);

        var hoy = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var responses = items.Select(m =>
        {
            ProveedorBancoDto? prov = null;
            if (m.BeneficiarioRef is Guid ProvId) proveedores.TryGetValue(ProvId, out prov);
            return new PagoACuentaAbiertoResponse(
                MovimientoId: m.Id,
                CuentaBancariaId: m.CuentaBancariaId,
                ProveedorId: m.BeneficiarioRef,
                ProveedorClave: prov?.Clave,
                ProveedorRazonSocial: prov?.RazonSocial,
                Monto: m.Monto,
                ImporteLigado: ligados.GetValueOrDefault(m.Id, 0m),
                Moneda: m.Moneda,
                FechaValor: m.FechaValor,
                ReferenciaBancaria: m.ReferenciaBancaria,
                Motivo: m.MotivoNoAplicado!,
                EstadoAplicacion: m.EstadoAplicacion,
                AntiguedadDias: hoy.DayNumber - m.FechaValor.DayNumber);
        }).ToList();

        return new PagedResponse<PagoACuentaAbiertoResponse>(responses, offset, limit, total);
    }
}
