using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Ports;
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
    int Limit = 50) : IRequest<PagedResponse<DepositoConfirmacionResponse>>, Millet.SharedKernel.Application.IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "tesoreria.documentos.leer-todas-sucursales";
    public string TipoDocumento => "deposito";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class DepositosHandler
    : IRequestHandler<DepositosQuery, PagedResponse<DepositoConfirmacionResponse>>
{
    private readonly TesoreriaDbContext _db;
    private readonly IClienteReadPort _clientes;
    private readonly ICorteNombresReadPort? _nombres;

    public DepositosHandler(TesoreriaDbContext db, IClienteReadPort clientes, ICorteNombresReadPort? nombres = null)
    {
        _db = db; _clientes = clientes; _nombres = nombres;
    }

    public async Task<PagedResponse<DepositoConfirmacionResponse>> Handle(
        DepositosQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.DepositosConfirmacion.Include(d => d.Fichas)
            .Where(x => query.DocumentosPermitidos == null || (query.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(x.Id)).AsNoTracking();
        if (query.Estado is EstadoDepositoConfirmacion estado) q = q.Where(d => d.Estado == estado);
        if (query.ClienteId is Guid cliente) q = q.Where(d => d.ClienteId == cliente);
        if (query.SoloPropuestas is true) q = q.Where(d => d.PropuestaCxcId != null);
        if (query.SoloPropuestas is false) q = q.Where(d => d.CajaSesionId != null);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(d => d.Estado == EstadoDepositoConfirmacion.Pendiente || d.Estado == EstadoDepositoConfirmacion.Parcial ? 0 : 1)
            .ThenByDescending(d => d.CreatedAt)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        var clienteIds = items.Where(d => d.ClienteId is not null)
            .Select(d => d.ClienteId!.Value).Distinct().ToList();
        var clientes = await _clientes.ObtenerVariosAsync(clienteIds, cancellationToken);

        var sesiones = items.Where(d => d.CajaSesionId is not null).Select(d => d.CajaSesionId!.Value).ToList();
        var diferencias = await _db.DiferenciasCorte.AsNoTracking()
            .Where(x => sesiones.Contains(x.CajaSesionId)).ToDictionaryAsync(x => x.CajaSesionId, cancellationToken);

        var cajeros = _nombres is null
            ? new Dictionary<Guid, string>()
            : await _nombres.NombresCajerosAsync(items.Where(d => d.CajeroId != null).Select(d => d.CajeroId!.Value).ToList(), cancellationToken);

        var responses = items.Select(d =>
        {
            ClienteRefDto? cliente = null;
            if (d.ClienteId is Guid id) clientes.TryGetValue(id, out cliente);
            DiferenciaCorte? diferencia = null;
            if (d.CajaSesionId is Guid sesion) diferencias.TryGetValue(sesion, out diferencia);
            var r = DepositoMapper.ToResponse(d, cliente?.Clave, cliente?.RazonSocial, diferencia);
            return d.CajeroId is Guid cajero && cajeros.TryGetValue(cajero, out var nombre) ? r with { CajeroNombre = nombre } : r;
        }).ToList();

        return new PagedResponse<DepositoConfirmacionResponse>(responses, offset, limit, total);
    }
}
