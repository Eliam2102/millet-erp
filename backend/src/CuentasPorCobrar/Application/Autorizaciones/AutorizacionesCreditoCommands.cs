using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Common;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.Autorizaciones;

// ============================================================================
// CXC-PR4: CRUD acotado de AutorizacionCredito (override consumible, calco
// de AutorizacionAperturaCaja). El supervisor es SIEMPRE el usuario actual
// (permiso liberacion.override en el endpoint) — nunca se acepta por body.
// ============================================================================

public sealed record AutorizacionCreditoResponse(
    Guid Id,
    Guid SupervisorUsuarioId,
    Guid BeneficiarioUsuarioId,
    string Motivo,
    string ClienteOPedidoRef,
    DateTimeOffset FechaAutorizacion,
    DateTimeOffset VigenteHasta,
    EstadoAutorizacionCredito Estado,
    Guid? DecisionLiberacionId,
    int Version);

internal static class AutorizacionCreditoMapper
{
    public static AutorizacionCreditoResponse ToResponse(AutorizacionCredito a) =>
        new(a.Id, a.SupervisorUsuarioId, a.BeneficiarioUsuarioId, a.Motivo,
            a.ClienteOPedidoRef, a.FechaAutorizacion, a.VigenteHasta,
            a.Estado, a.DecisionLiberacionId, a.Version);
}

// --------------------------------------------------- Crear

public sealed record CrearAutorizacionCreditoCommand(
    Guid BeneficiarioUsuarioId,
    string Motivo,
    string ClienteOPedidoRef,
    int VigenciaHoras = 24) : IRequest<AutorizacionCreditoResponse>;

public sealed class CrearAutorizacionCreditoValidator : AbstractValidator<CrearAutorizacionCreditoCommand>
{
    public CrearAutorizacionCreditoValidator()
    {
        RuleFor(c => c.BeneficiarioUsuarioId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(254);
        RuleFor(c => c.ClienteOPedidoRef).NotEmpty().MaximumLength(80);
        RuleFor(c => c.VigenciaHoras).InclusiveBetween(1, 24);
    }
}

public sealed class CrearAutorizacionCreditoHandler
    : IRequestHandler<CrearAutorizacionCreditoCommand, AutorizacionCreditoResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public CrearAutorizacionCreditoHandler(
        CuentasPorCobrarDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser; _clock = clock;
    }

    public async Task<AutorizacionCreditoResponse> Handle(
        CrearAutorizacionCreditoCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid supervisorId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al supervisor.");

        var autorizacion = AutorizacionCredito.Crear(
            empresaId: empresaId,
            supervisorUsuarioId: supervisorId,
            beneficiarioUsuarioId: command.BeneficiarioUsuarioId,
            motivo: command.Motivo,
            clienteOPedidoRef: command.ClienteOPedidoRef,
            ahora: _clock.UtcNow,
            vigencia: TimeSpan.FromHours(command.VigenciaHoras));

        _db.AutorizacionesCredito.Add(autorizacion);
        await _db.SaveChangesAsync(cancellationToken);
        return AutorizacionCreditoMapper.ToResponse(autorizacion);
    }
}

// --------------------------------------------------- Cancelar

public sealed record CancelarAutorizacionCreditoCommand(Guid Id, int VersionEsperada)
    : IRequest<AutorizacionCreditoResponse>;

public sealed class CancelarAutorizacionCreditoHandler
    : IRequestHandler<CancelarAutorizacionCreditoCommand, AutorizacionCreditoResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    public CancelarAutorizacionCreditoHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<AutorizacionCreditoResponse> Handle(
        CancelarAutorizacionCreditoCommand command, CancellationToken cancellationToken)
    {
        var a = await _db.AutorizacionesCredito
            .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("AC_NO_ENCONTRADA",
                $"No se encontró la autorización '{command.Id}'.");
        if (a.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(AutorizacionCredito), a.Id);

        a.Cancelar();
        await _db.SaveChangesAsync(cancellationToken);
        return AutorizacionCreditoMapper.ToResponse(a);
    }
}

// --------------------------------------------------- Listar

public sealed record ListarAutorizacionesCreditoQuery(
    EstadoAutorizacionCredito? Estado = null,
    Guid? BeneficiarioUsuarioId = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<AutorizacionCreditoResponse>>;

public sealed class ListarAutorizacionesCreditoHandler
    : IRequestHandler<ListarAutorizacionesCreditoQuery, PagedResponse<AutorizacionCreditoResponse>>
{
    private readonly CuentasPorCobrarDbContext _db;
    public ListarAutorizacionesCreditoHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<PagedResponse<AutorizacionCreditoResponse>> Handle(
        ListarAutorizacionesCreditoQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.AutorizacionesCredito.AsNoTracking();
        if (query.Estado is EstadoAutorizacionCredito e) q = q.Where(a => a.Estado == e);
        if (query.BeneficiarioUsuarioId is Guid ben) q = q.Where(a => a.BeneficiarioUsuarioId == ben);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(a => a.FechaAutorizacion)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        return new PagedResponse<AutorizacionCreditoResponse>(
            items.Select(AutorizacionCreditoMapper.ToResponse).ToList(),
            offset, limit, total);
    }
}
