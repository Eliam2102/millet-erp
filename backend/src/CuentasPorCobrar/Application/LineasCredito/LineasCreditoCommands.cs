using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Common;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.LineasCredito;

// ============================================================================
// CXC-PR1: CRUD acotado de LineaCredito (§4.2 del 01-diseño): crear/editar
// límite+plazo, bloquear/desbloquear, bandeja paginada. La evaluación de
// crédito disponible entra en CXC-PR2.
// ============================================================================

public sealed record LineaCreditoResponse(
    Guid Id,
    Guid ClienteId,
    string Moneda,
    decimal Limite,
    OrigenLineaCredito Origen,
    int PlazoDias,
    string? Clasificacion,
    EstadoLineaCredito Estado,
    string? MotivoBloqueo,
    int Version);

internal static class LineaCreditoMapper
{
    public static LineaCreditoResponse ToResponse(LineaCredito l) =>
        new(l.Id, l.ClienteId, l.Moneda, l.Limite, l.Origen, l.PlazoDias,
            l.Clasificacion, l.Estado, l.MotivoBloqueo, l.Version);
}

// --------------------------------------------------- Crear

public sealed record CrearLineaCreditoCommand(
    Guid ClienteId,
    string Moneda,
    decimal Limite,
    OrigenLineaCredito Origen,
    int PlazoDias,
    string? Clasificacion = null) : IRequest<LineaCreditoResponse>;

public sealed class CrearLineaCreditoValidator : AbstractValidator<CrearLineaCreditoCommand>
{
    public CrearLineaCreditoValidator()
    {
        RuleFor(c => c.ClienteId).NotEmpty();
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        RuleFor(c => c.Limite).GreaterThan(0);
        RuleFor(c => c.Origen).IsInEnum();
        RuleFor(c => c.PlazoDias).GreaterThan(0).LessThanOrEqualTo(365);
        RuleFor(c => c.Clasificacion)
            .Must(c => c is null or "A" or "B" or "C" or "E")
            .WithMessage("La clasificación debe ser A, B, C o E.");
    }
}

public sealed class CrearLineaCreditoHandler : IRequestHandler<CrearLineaCreditoCommand, LineaCreditoResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public CrearLineaCreditoHandler(CuentasPorCobrarDbContext db, ICurrentEmpresaContext currentEmpresa)
    {
        _db = db; _currentEmpresa = currentEmpresa;
    }

    public async Task<LineaCreditoResponse> Handle(CrearLineaCreditoCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");

        // Invariante §4.2: una sola línea Activa por (cliente, moneda). El
        // índice único parcial es el backstop; aquí se valida para devolver
        // un 422 legible en vez de un error de constraint.
        var yaExiste = await _db.LineasCredito
            .AnyAsync(l => l.ClienteId == command.ClienteId
                        && l.Moneda == command.Moneda
                        && l.Estado == EstadoLineaCredito.Activa,
                cancellationToken);
        if (yaExiste)
            throw new BusinessRuleException("LC_DUPLICADA",
                $"El cliente ya tiene una línea de crédito activa en {command.Moneda}.");

        var linea = LineaCredito.Crear(
            empresaId: empresaId,
            clienteId: command.ClienteId,
            moneda: command.Moneda,
            limite: command.Limite,
            origen: command.Origen,
            plazoDias: command.PlazoDias,
            clasificacion: command.Clasificacion);

        _db.LineasCredito.Add(linea);
        await _db.SaveChangesAsync(cancellationToken);
        return LineaCreditoMapper.ToResponse(linea);
    }
}

// --------------------------------------------------- Actualizar

public sealed record ActualizarLineaCreditoCommand(
    Guid Id, int VersionEsperada,
    decimal Limite, int PlazoDias, string? Clasificacion = null) : IRequest<LineaCreditoResponse>;

public sealed class ActualizarLineaCreditoValidator : AbstractValidator<ActualizarLineaCreditoCommand>
{
    public ActualizarLineaCreditoValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Limite).GreaterThan(0);
        RuleFor(c => c.PlazoDias).GreaterThan(0).LessThanOrEqualTo(365);
        RuleFor(c => c.Clasificacion)
            .Must(c => c is null or "A" or "B" or "C" or "E")
            .WithMessage("La clasificación debe ser A, B, C o E.");
    }
}

public sealed class ActualizarLineaCreditoHandler : IRequestHandler<ActualizarLineaCreditoCommand, LineaCreditoResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    public ActualizarLineaCreditoHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<LineaCreditoResponse> Handle(ActualizarLineaCreditoCommand command, CancellationToken cancellationToken)
    {
        var l = await _db.LineasCredito.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("LC_NO_ENCONTRADA",
                $"No se encontró la línea de crédito '{command.Id}'.");
        if (l.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(LineaCredito), l.Id);

        l.ActualizarDatos(command.Limite, command.PlazoDias, command.Clasificacion);
        await _db.SaveChangesAsync(cancellationToken);
        return LineaCreditoMapper.ToResponse(l);
    }
}

// --------------------------------------------------- Bloquear / Desbloquear

public sealed record BloquearLineaCreditoCommand(Guid Id, int VersionEsperada, string Motivo)
    : IRequest<LineaCreditoResponse>;

public sealed class BloquearLineaCreditoValidator : AbstractValidator<BloquearLineaCreditoCommand>
{
    public BloquearLineaCreditoValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class BloquearLineaCreditoHandler : IRequestHandler<BloquearLineaCreditoCommand, LineaCreditoResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    public BloquearLineaCreditoHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<LineaCreditoResponse> Handle(BloquearLineaCreditoCommand command, CancellationToken cancellationToken)
    {
        var l = await _db.LineasCredito.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("LC_NO_ENCONTRADA",
                $"No se encontró la línea de crédito '{command.Id}'.");
        if (l.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(LineaCredito), l.Id);

        l.Bloquear(command.Motivo);
        await _db.SaveChangesAsync(cancellationToken);
        return LineaCreditoMapper.ToResponse(l);
    }
}

public sealed record DesbloquearLineaCreditoCommand(Guid Id, int VersionEsperada)
    : IRequest<LineaCreditoResponse>;

public sealed class DesbloquearLineaCreditoHandler : IRequestHandler<DesbloquearLineaCreditoCommand, LineaCreditoResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    public DesbloquearLineaCreditoHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<LineaCreditoResponse> Handle(DesbloquearLineaCreditoCommand command, CancellationToken cancellationToken)
    {
        var l = await _db.LineasCredito.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("LC_NO_ENCONTRADA",
                $"No se encontró la línea de crédito '{command.Id}'.");
        if (l.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(LineaCredito), l.Id);

        // La restricción "una Activa por (cliente, moneda)" también aplica al
        // desbloquear: pudo haberse creado otra línea mientras ésta estaba
        // bloqueada.
        var hayOtraActiva = await _db.LineasCredito
            .AnyAsync(x => x.Id != l.Id
                        && x.ClienteId == l.ClienteId
                        && x.Moneda == l.Moneda
                        && x.Estado == EstadoLineaCredito.Activa,
                cancellationToken);
        if (hayOtraActiva)
            throw new BusinessRuleException("LC_DUPLICADA",
                $"El cliente ya tiene otra línea de crédito activa en {l.Moneda} — bloquéala antes de desbloquear ésta.");

        l.Desbloquear();
        await _db.SaveChangesAsync(cancellationToken);
        return LineaCreditoMapper.ToResponse(l);
    }
}

// --------------------------------------------------- Listar / Obtener

public sealed record ListarLineasCreditoQuery(
    Guid? ClienteId = null,
    EstadoLineaCredito? Estado = null,
    string? Moneda = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<LineaCreditoResponse>>;

public sealed class ListarLineasCreditoHandler : IRequestHandler<ListarLineasCreditoQuery, PagedResponse<LineaCreditoResponse>>
{
    private readonly CuentasPorCobrarDbContext _db;
    public ListarLineasCreditoHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<PagedResponse<LineaCreditoResponse>> Handle(
        ListarLineasCreditoQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.LineasCredito.AsNoTracking();
        if (query.ClienteId is Guid cliente) q = q.Where(l => l.ClienteId == cliente);
        if (query.Estado is EstadoLineaCredito e) q = q.Where(l => l.Estado == e);
        if (!string.IsNullOrWhiteSpace(query.Moneda)) q = q.Where(l => l.Moneda == query.Moneda);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(l => l.ClienteId).ThenBy(l => l.Moneda)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        return new PagedResponse<LineaCreditoResponse>(
            items.Select(LineaCreditoMapper.ToResponse).ToList(),
            offset, limit, total);
    }
}

public sealed record ObtenerLineaCreditoQuery(Guid Id) : IRequest<LineaCreditoResponse>;

public sealed class ObtenerLineaCreditoHandler : IRequestHandler<ObtenerLineaCreditoQuery, LineaCreditoResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    public ObtenerLineaCreditoHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<LineaCreditoResponse> Handle(ObtenerLineaCreditoQuery query, CancellationToken cancellationToken)
    {
        var l = await _db.LineasCredito.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException("LC_NO_ENCONTRADA",
                $"No se encontró la línea de crédito '{query.Id}'.");
        return LineaCreditoMapper.ToResponse(l);
    }
}
