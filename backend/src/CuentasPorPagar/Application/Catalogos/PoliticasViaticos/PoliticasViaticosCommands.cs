using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Catalogos.PoliticasViaticos;

// ============================================================================
// F7-PR3: CRUD del catálogo `politicas_viaticos` (§7.4.2, §13.1 punto 3).
// Mantenimiento por RH + Dirección.
// ============================================================================

// --------------------------------------------------- Crear

public sealed record CrearPoliticaViaticosCommand(
    Guid PuestoId,
    TipoDestinoViatico TipoDestino,
    decimal MontoMaxDia,
    int DiasMax,
    string Moneda) : IRequest<PoliticaViaticosResponse>;

public sealed record PoliticaViaticosResponse(
    Guid Id,
    Guid PuestoId,
    TipoDestinoViatico TipoDestino,
    decimal MontoMaxDia,
    int DiasMax,
    string Moneda,
    int Version);

public sealed class CrearPoliticaViaticosValidator : AbstractValidator<CrearPoliticaViaticosCommand>
{
    public CrearPoliticaViaticosValidator()
    {
        RuleFor(c => c.PuestoId).NotEmpty();
        RuleFor(c => c.MontoMaxDia).GreaterThan(0);
        RuleFor(c => c.DiasMax).GreaterThan(0);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
    }
}

public sealed class CrearPoliticaViaticosHandler
    : IRequestHandler<CrearPoliticaViaticosCommand, PoliticaViaticosResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public CrearPoliticaViaticosHandler(CuentasPorPagarDbContext db, ICurrentEmpresaContext currentEmpresa)
    {
        _db = db; _currentEmpresa = currentEmpresa;
    }

    public async Task<PoliticaViaticosResponse> Handle(
        CrearPoliticaViaticosCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");

        var existe = await _db.PoliticasViaticos
            .AnyAsync(p => p.PuestoId == command.PuestoId
                           && p.TipoDestino == command.TipoDestino, cancellationToken);
        if (existe)
        {
            throw new BusinessRuleException(
                "POLITICA_DUPLICADA",
                $"Ya existe política para puesto={command.PuestoId} destino={command.TipoDestino}.");
        }

        var p = PoliticaViaticos.Crear(
            empresaId: empresaId,
            puestoId: command.PuestoId,
            tipoDestino: command.TipoDestino,
            montoMaxDia: command.MontoMaxDia,
            diasMax: command.DiasMax,
            moneda: command.Moneda);

        _db.PoliticasViaticos.Add(p);
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(p);
    }

    internal static PoliticaViaticosResponse ToResponse(PoliticaViaticos p) =>
        new(p.Id, p.PuestoId, p.TipoDestino, p.MontoMaxDia, p.DiasMax, p.Moneda, p.Version);
}

// --------------------------------------------------- Actualizar

public sealed record ActualizarPoliticaViaticosCommand(
    Guid Id,
    int VersionEsperada,
    decimal MontoMaxDia,
    int DiasMax,
    string Moneda) : IRequest<PoliticaViaticosResponse>;

public sealed class ActualizarPoliticaViaticosValidator : AbstractValidator<ActualizarPoliticaViaticosCommand>
{
    public ActualizarPoliticaViaticosValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.MontoMaxDia).GreaterThan(0);
        RuleFor(c => c.DiasMax).GreaterThan(0);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
    }
}

public sealed class ActualizarPoliticaViaticosHandler
    : IRequestHandler<ActualizarPoliticaViaticosCommand, PoliticaViaticosResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    public ActualizarPoliticaViaticosHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<PoliticaViaticosResponse> Handle(
        ActualizarPoliticaViaticosCommand command, CancellationToken cancellationToken)
    {
        var p = await _db.PoliticasViaticos.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("POLITICA_NO_ENCONTRADA",
                $"No se encontró la política '{command.Id}'.");
        if (p.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(PoliticaViaticos), p.Id);

        p.Actualizar(command.MontoMaxDia, command.DiasMax, command.Moneda);
        await _db.SaveChangesAsync(cancellationToken);
        return CrearPoliticaViaticosHandler.ToResponse(p);
    }
}

// --------------------------------------------------- Listar

public sealed record ListarPoliticasViaticosQuery(
    Guid? PuestoId = null,
    TipoDestinoViatico? TipoDestino = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<PoliticaViaticosResponse>>;

public sealed class ListarPoliticasViaticosHandler
    : IRequestHandler<ListarPoliticasViaticosQuery, PagedResponse<PoliticaViaticosResponse>>
{
    private readonly CuentasPorPagarDbContext _db;
    public ListarPoliticasViaticosHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<PagedResponse<PoliticaViaticosResponse>> Handle(
        ListarPoliticasViaticosQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.PoliticasViaticos.AsNoTracking();
        if (query.PuestoId is Guid p) q = q.Where(x => x.PuestoId == p);
        if (query.TipoDestino is TipoDestinoViatico td) q = q.Where(x => x.TipoDestino == td);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(x => x.PuestoId).ThenBy(x => x.TipoDestino)
            .Skip(offset).Take(limit)
            .Select(x => new PoliticaViaticosResponse(
                x.Id, x.PuestoId, x.TipoDestino, x.MontoMaxDia, x.DiasMax, x.Moneda, x.Version))
            .ToListAsync(cancellationToken);

        return new PagedResponse<PoliticaViaticosResponse>(items, offset, limit, total);
    }
}
