using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Catalogos.AprobadoresLimites;

// ============================================================================
// F7-PR3: CRUD del catálogo `aprobadores_limites` (§5.5, §13.1 punto 2).
// Mantenimiento por RH o por el responsable de CxP.
// ============================================================================

// --------------------------------------------------- Crear

public sealed record CrearAprobadorLimiteCommand(
    Guid EmpleadoId,
    TipoGastoAprobador TipoGasto,
    decimal MontoMax,
    string Moneda,
    DateOnly VigenciaDesde,
    DateOnly? VigenciaHasta) : IRequest<AprobadorLimiteResponse>;

public sealed record AprobadorLimiteResponse(
    Guid Id,
    Guid EmpleadoId,
    TipoGastoAprobador TipoGasto,
    decimal MontoMax,
    string Moneda,
    DateOnly VigenciaDesde,
    DateOnly? VigenciaHasta,
    int Version);

public sealed class CrearAprobadorLimiteValidator : AbstractValidator<CrearAprobadorLimiteCommand>
{
    public CrearAprobadorLimiteValidator()
    {
        RuleFor(c => c.EmpleadoId).NotEmpty();
        RuleFor(c => c.MontoMax).GreaterThan(0);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
    }
}

public sealed class CrearAprobadorLimiteHandler
    : IRequestHandler<CrearAprobadorLimiteCommand, AprobadorLimiteResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public CrearAprobadorLimiteHandler(CuentasPorPagarDbContext db, ICurrentEmpresaContext currentEmpresa)
    {
        _db = db; _currentEmpresa = currentEmpresa;
    }

    public async Task<AprobadorLimiteResponse> Handle(
        CrearAprobadorLimiteCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");

        var aprobador = AprobadorLimite.Crear(
            empresaId: empresaId,
            empleadoId: command.EmpleadoId,
            tipoGasto: command.TipoGasto,
            montoMax: command.MontoMax,
            moneda: command.Moneda,
            vigenciaDesde: command.VigenciaDesde,
            vigenciaHasta: command.VigenciaHasta);

        _db.AprobadoresLimites.Add(aprobador);
        await _db.SaveChangesAsync(cancellationToken);

        return ToResponse(aprobador);
    }

    internal static AprobadorLimiteResponse ToResponse(AprobadorLimite a) =>
        new(a.Id, a.EmpleadoId, a.TipoGasto, a.MontoMax, a.Moneda, a.VigenciaDesde, a.VigenciaHasta, a.Version);
}

// --------------------------------------------------- Actualizar

public sealed record ActualizarAprobadorLimiteCommand(
    Guid Id,
    int VersionEsperada,
    decimal MontoMax,
    string Moneda) : IRequest<AprobadorLimiteResponse>;

public sealed class ActualizarAprobadorLimiteValidator
    : AbstractValidator<ActualizarAprobadorLimiteCommand>
{
    public ActualizarAprobadorLimiteValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.MontoMax).GreaterThan(0);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
    }
}

public sealed class ActualizarAprobadorLimiteHandler
    : IRequestHandler<ActualizarAprobadorLimiteCommand, AprobadorLimiteResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    public ActualizarAprobadorLimiteHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<AprobadorLimiteResponse> Handle(
        ActualizarAprobadorLimiteCommand command, CancellationToken cancellationToken)
    {
        var a = await _db.AprobadoresLimites.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("APROBADOR_NO_ENCONTRADO",
                $"No se encontró el aprobador '{command.Id}'.");
        if (a.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(AprobadorLimite), a.Id);

        a.ActualizarLimite(command.MontoMax, command.Moneda);
        await _db.SaveChangesAsync(cancellationToken);
        return CrearAprobadorLimiteHandler.ToResponse(a);
    }
}

// --------------------------------------------------- Cerrar

public sealed record CerrarAprobadorLimiteCommand(Guid Id, int VersionEsperada, DateOnly Fecha)
    : IRequest<AprobadorLimiteResponse>;

public sealed class CerrarAprobadorLimiteValidator : AbstractValidator<CerrarAprobadorLimiteCommand>
{
    public CerrarAprobadorLimiteValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
    }
}

public sealed class CerrarAprobadorLimiteHandler
    : IRequestHandler<CerrarAprobadorLimiteCommand, AprobadorLimiteResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    public CerrarAprobadorLimiteHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<AprobadorLimiteResponse> Handle(
        CerrarAprobadorLimiteCommand command, CancellationToken cancellationToken)
    {
        var a = await _db.AprobadoresLimites.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("APROBADOR_NO_ENCONTRADO",
                $"No se encontró el aprobador '{command.Id}'.");
        if (a.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(AprobadorLimite), a.Id);

        a.Cerrar(command.Fecha);
        await _db.SaveChangesAsync(cancellationToken);
        return CrearAprobadorLimiteHandler.ToResponse(a);
    }
}

// --------------------------------------------------- Listar

public sealed record ListarAprobadoresLimitesQuery(
    Guid? EmpleadoId = null,
    TipoGastoAprobador? TipoGasto = null,
    bool SoloVigentes = true,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<AprobadorLimiteResponse>>;

public sealed class ListarAprobadoresLimitesHandler
    : IRequestHandler<ListarAprobadoresLimitesQuery, PagedResponse<AprobadorLimiteResponse>>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;

    public ListarAprobadoresLimitesHandler(CuentasPorPagarDbContext db, IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task<PagedResponse<AprobadorLimiteResponse>> Handle(
        ListarAprobadoresLimitesQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);
        var hoy = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        var q = _db.AprobadoresLimites.AsNoTracking();
        if (query.EmpleadoId is Guid e) q = q.Where(a => a.EmpleadoId == e);
        if (query.TipoGasto is TipoGastoAprobador t) q = q.Where(a => a.TipoGasto == t);
        if (query.SoloVigentes)
        {
            q = q.Where(a =>
                a.VigenciaDesde <= hoy
                && (a.VigenciaHasta == null || a.VigenciaHasta >= hoy));
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(a => a.EmpleadoId).ThenBy(a => a.TipoGasto)
            .Skip(offset).Take(limit)
            .Select(a => new AprobadorLimiteResponse(
                a.Id, a.EmpleadoId, a.TipoGasto, a.MontoMax, a.Moneda,
                a.VigenciaDesde, a.VigenciaHasta, a.Version))
            .ToListAsync(cancellationToken);

        return new PagedResponse<AprobadorLimiteResponse>(items, offset, limit, total);
    }
}
