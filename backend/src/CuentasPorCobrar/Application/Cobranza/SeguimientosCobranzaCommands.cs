using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Common;
using Millet.CuentasPorCobrar.Domain.Cobranza;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.Cobranza;

// ============================================================================
// CXC-PR5: registro append-only de gestiones de cobranza + bandeja por
// cliente (§1 del 00-levantamiento: cada gestión deja rastro; no hay
// edición ni borrado). El usuario gestor es SIEMPRE el usuario actual.
// ============================================================================

public sealed record SeguimientoCobranzaResponse(
    Guid Id,
    Guid ClienteId,
    DateTimeOffset Fecha,
    Guid UsuarioId,
    CanalCobranza Canal,
    ResultadoCobranza Resultado,
    decimal? MontoComprometido,
    DateOnly? FechaComprometida,
    string Nota);

internal static class SeguimientoCobranzaMapper
{
    public static SeguimientoCobranzaResponse ToResponse(SeguimientoCobranza s) =>
        new(s.Id, s.ClienteId, s.Fecha, s.UsuarioId, s.Canal, s.Resultado,
            s.MontoComprometido, s.FechaComprometida, s.Nota);
}

// --------------------------------------------------- Registrar

public sealed record RegistrarSeguimientoCobranzaCommand(
    Guid ClienteId,
    CanalCobranza Canal,
    ResultadoCobranza Resultado,
    decimal? MontoComprometido,
    DateOnly? FechaComprometida,
    string Nota) : IRequest<SeguimientoCobranzaResponse>;

public sealed class RegistrarSeguimientoCobranzaValidator
    : AbstractValidator<RegistrarSeguimientoCobranzaCommand>
{
    public RegistrarSeguimientoCobranzaValidator()
    {
        RuleFor(c => c.ClienteId).NotEmpty();
        RuleFor(c => c.Canal).IsInEnum();
        RuleFor(c => c.Resultado).IsInEnum();
        RuleFor(c => c.Nota).NotEmpty().MaximumLength(2000);

        When(c => c.Resultado == ResultadoCobranza.PromesaPago, () =>
        {
            RuleFor(c => c.MontoComprometido).NotNull().GreaterThan(0);
            RuleFor(c => c.FechaComprometida).NotNull();
        });
    }
}

public sealed class RegistrarSeguimientoCobranzaHandler
    : IRequestHandler<RegistrarSeguimientoCobranzaCommand, SeguimientoCobranzaResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public RegistrarSeguimientoCobranzaHandler(
        CuentasPorCobrarDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser; _clock = clock;
    }

    public async Task<SeguimientoCobranzaResponse> Handle(
        RegistrarSeguimientoCobranzaCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al usuario gestor.");

        var seguimiento = SeguimientoCobranza.Registrar(
            empresaId: empresaId,
            clienteId: command.ClienteId,
            usuarioId: usuarioId,
            canal: command.Canal,
            resultado: command.Resultado,
            montoComprometido: command.MontoComprometido,
            fechaComprometida: command.FechaComprometida,
            nota: command.Nota,
            fecha: _clock.UtcNow);

        _db.SeguimientosCobranza.Add(seguimiento);
        await _db.SaveChangesAsync(cancellationToken);
        return SeguimientoCobranzaMapper.ToResponse(seguimiento);
    }
}

// --------------------------------------------------- Listar

public sealed record ListarSeguimientosCobranzaQuery(
    Guid ClienteId,
    ResultadoCobranza? Resultado = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<SeguimientoCobranzaResponse>>;

public sealed class ListarSeguimientosCobranzaHandler
    : IRequestHandler<ListarSeguimientosCobranzaQuery, PagedResponse<SeguimientoCobranzaResponse>>
{
    private readonly CuentasPorCobrarDbContext _db;
    public ListarSeguimientosCobranzaHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<PagedResponse<SeguimientoCobranzaResponse>> Handle(
        ListarSeguimientosCobranzaQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.SeguimientosCobranza.AsNoTracking()
            .Where(s => s.ClienteId == query.ClienteId);
        if (query.Resultado is ResultadoCobranza r) q = q.Where(s => s.Resultado == r);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(s => s.Fecha)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        return new PagedResponse<SeguimientoCobranzaResponse>(
            items.Select(SeguimientoCobranzaMapper.ToResponse).ToList(),
            offset, limit, total);
    }
}
