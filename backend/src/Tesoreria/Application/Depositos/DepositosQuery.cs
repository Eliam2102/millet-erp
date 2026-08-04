using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Depositos;

// ============================================================================
// TES-PR7: bandeja de depósitos por confirmar (§7.2, patrón P2 server-side).
// Incluye propuestas de CxC y expectativas de Caja; nombres de cliente vía
// IClienteReadPort (ADR-0042).
// ============================================================================

public sealed record DepositosQuery(
    EstadoDepositoConfirmacion? Estado = null,
    Guid? ClienteId = null,
    bool? SoloPropuestas = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<DepositoConfirmacionResponse>>;

public sealed class DepositosHandler
    : IRequestHandler<DepositosQuery, PagedResponse<DepositoConfirmacionResponse>>
{
    private readonly TesoreriaDbContext _db;
    private readonly IClienteReadPort _clientes;

    public DepositosHandler(TesoreriaDbContext db, IClienteReadPort clientes)
    {
        _db = db; _clientes = clientes;
    }

    public async Task<PagedResponse<DepositoConfirmacionResponse>> Handle(
        DepositosQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.DepositosConfirmacion.AsNoTracking();
        if (query.Estado is EstadoDepositoConfirmacion estado) q = q.Where(d => d.Estado == estado);
        if (query.ClienteId is Guid cliente) q = q.Where(d => d.ClienteId == cliente);
        if (query.SoloPropuestas is true) q = q.Where(d => d.PropuestaCxcId != null);
        if (query.SoloPropuestas is false) q = q.Where(d => d.CajaSesionId != null);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(d => d.Estado == EstadoDepositoConfirmacion.Pendiente ? 0 : 1)
            .ThenByDescending(d => d.CreatedAt)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        var clienteIds = items.Where(d => d.ClienteId is not null)
            .Select(d => d.ClienteId!.Value).Distinct().ToList();
        var clientes = await _clientes.ObtenerVariosAsync(clienteIds, cancellationToken);

        var responses = items.Select(d =>
        {
            ClienteRefDto? cliente = null;
            if (d.ClienteId is Guid id) clientes.TryGetValue(id, out cliente);
            return DepositoMapper.ToResponse(d, cliente?.Clave, cliente?.RazonSocial);
        }).ToList();

        return new PagedResponse<DepositoConfirmacionResponse>(responses, offset, limit, total);
    }
}
