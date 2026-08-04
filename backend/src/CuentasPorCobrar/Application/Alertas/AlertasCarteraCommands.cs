using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Common;
using Millet.CuentasPorCobrar.Domain.Alertas;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.Alertas;

// ============================================================================
// CXC-PR8: bandeja de alertas + atender.
// ============================================================================

public sealed record AlertaCarteraResponse(
    Guid Id,
    Guid ClienteId,
    TipoAlertaCartera Tipo,
    string Moneda,
    string Detalle,
    DateTimeOffset DisparadaEn,
    bool Atendida,
    Guid? AtendidaPor,
    DateTimeOffset? AtendidaEn,
    int Version);

internal static class AlertaCarteraMapper
{
    public static AlertaCarteraResponse ToResponse(AlertaCartera a) =>
        new(a.Id, a.ClienteId, a.Tipo, a.Moneda, a.Detalle, a.DisparadaEn,
            a.Atendida, a.AtendidaPor, a.AtendidaEn, a.Version);
}

// --------------------------------------------------- Listar

public sealed record ListarAlertasCarteraQuery(
    bool? Atendida = null,
    Guid? ClienteId = null,
    TipoAlertaCartera? Tipo = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<AlertaCarteraResponse>>;

public sealed class ListarAlertasCarteraHandler
    : IRequestHandler<ListarAlertasCarteraQuery, PagedResponse<AlertaCarteraResponse>>
{
    private readonly CuentasPorCobrarDbContext _db;
    public ListarAlertasCarteraHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<PagedResponse<AlertaCarteraResponse>> Handle(
        ListarAlertasCarteraQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.AlertasCartera.AsNoTracking();
        if (query.Atendida is bool at) q = q.Where(a => a.Atendida == at);
        if (query.ClienteId is Guid c) q = q.Where(a => a.ClienteId == c);
        if (query.Tipo is TipoAlertaCartera t) q = q.Where(a => a.Tipo == t);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(a => a.DisparadaEn)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        return new PagedResponse<AlertaCarteraResponse>(
            items.Select(AlertaCarteraMapper.ToResponse).ToList(),
            offset, limit, total);
    }
}

// --------------------------------------------------- Atender

public sealed record AtenderAlertaCarteraCommand(Guid Id, int VersionEsperada)
    : IRequest<AlertaCarteraResponse>;

public sealed class AtenderAlertaCarteraHandler
    : IRequestHandler<AtenderAlertaCarteraCommand, AlertaCarteraResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public AtenderAlertaCarteraHandler(
        CuentasPorCobrarDbContext db, ICurrentUserContext currentUser, IClock clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public async Task<AlertaCarteraResponse> Handle(
        AtenderAlertaCarteraCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario.");

        var a = await _db.AlertasCartera
            .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("AL_NO_ENCONTRADA",
                $"No se encontró la alerta '{command.Id}'.");
        if (a.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(AlertaCartera), a.Id);

        a.Atender(usuarioId, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return AlertaCarteraMapper.ToResponse(a);
    }
}
