using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.TarjetaCredito.Tarjetas;

// ============================================================================
// F7-PR4: CRUD master de Tarjeta + usuarios autorizados (§3, §4 anexo TC).
// ============================================================================

public sealed record TarjetaResponse(
    Guid Id,
    string Emisora,
    string PerfilParser,
    string NumeroEnmascarado,
    string NombreAlias,
    Guid TitularId,
    Guid BancoProveedorId,
    decimal LimiteCreditoMxn,
    string MonedaDefault,
    short DiaCorte,
    short DiaLimitePago,
    EstadoTarjeta Estado,
    DateOnly? FechaBloqueo,
    string? MotivoBloqueo,
    DateOnly VigenciaDesde,
    DateOnly? VigenciaHasta,
    int Version);

internal static class TarjetaMapper
{
    public static TarjetaResponse ToResponse(Tarjeta t) =>
        new(t.Id, t.Emisora, t.PerfilParser, t.Numero.Valor, t.NombreAlias,
            t.TitularId, t.BancoProveedorId, t.LimiteCreditoMxn, t.MonedaDefault,
            t.DiaCorte, t.DiaLimitePago, t.Estado, t.FechaBloqueo, t.MotivoBloqueo,
            t.VigenciaDesde, t.VigenciaHasta, t.Version);
}

// --------------------------------------------------- Crear

public sealed record CrearTarjetaCommand(
    string Emisora,
    string PerfilParser,
    string UltimosCuatro,
    string NombreAlias,
    Guid TitularId,
    Guid BancoProveedorId,
    decimal LimiteCreditoMxn,
    string MonedaDefault,
    short DiaCorte,
    short DiaLimitePago,
    DateOnly VigenciaDesde) : IRequest<TarjetaResponse>;

public sealed class CrearTarjetaValidator : AbstractValidator<CrearTarjetaCommand>
{
    public CrearTarjetaValidator()
    {
        RuleFor(c => c.Emisora).NotEmpty().MaximumLength(60);
        RuleFor(c => c.PerfilParser).NotEmpty().MaximumLength(40);
        RuleFor(c => c.UltimosCuatro).NotEmpty().Length(4);
        RuleFor(c => c.NombreAlias).NotEmpty().MaximumLength(120);
        RuleFor(c => c.TitularId).NotEmpty();
        RuleFor(c => c.BancoProveedorId).NotEmpty();
        RuleFor(c => c.LimiteCreditoMxn).GreaterThan(0);
        RuleFor(c => c.MonedaDefault).NotEmpty().Length(3);
        RuleFor(c => c.DiaCorte).InclusiveBetween((short)1, (short)31);
        RuleFor(c => c.DiaLimitePago).InclusiveBetween((short)1, (short)60);
    }
}

public sealed class CrearTarjetaHandler : IRequestHandler<CrearTarjetaCommand, TarjetaResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public CrearTarjetaHandler(CuentasPorPagarDbContext db, ICurrentEmpresaContext currentEmpresa)
    {
        _db = db; _currentEmpresa = currentEmpresa;
    }

    public async Task<TarjetaResponse> Handle(CrearTarjetaCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");

        var numero = NumeroTarjetaEnmascarado.FromUltimosCuatro(command.UltimosCuatro);

        var tarjeta = Tarjeta.Crear(
            empresaId: empresaId,
            emisora: command.Emisora,
            perfilParser: command.PerfilParser,
            numero: numero,
            nombreAlias: command.NombreAlias,
            titularId: command.TitularId,
            bancoProveedorId: command.BancoProveedorId,
            limiteCreditoMxn: command.LimiteCreditoMxn,
            monedaDefault: command.MonedaDefault,
            diaCorte: command.DiaCorte,
            diaLimitePago: command.DiaLimitePago,
            vigenciaDesde: command.VigenciaDesde);

        _db.TarjetasCredito.Add(tarjeta);
        await _db.SaveChangesAsync(cancellationToken);
        return TarjetaMapper.ToResponse(tarjeta);
    }
}

// --------------------------------------------------- Actualizar

public sealed record ActualizarTarjetaCommand(
    Guid Id, int VersionEsperada,
    string NombreAlias, decimal LimiteCreditoMxn,
    short DiaCorte, short DiaLimitePago) : IRequest<TarjetaResponse>;

public sealed class ActualizarTarjetaValidator : AbstractValidator<ActualizarTarjetaCommand>
{
    public ActualizarTarjetaValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.NombreAlias).NotEmpty().MaximumLength(120);
        RuleFor(c => c.LimiteCreditoMxn).GreaterThan(0);
        RuleFor(c => c.DiaCorte).InclusiveBetween((short)1, (short)31);
        RuleFor(c => c.DiaLimitePago).InclusiveBetween((short)1, (short)60);
    }
}

public sealed class ActualizarTarjetaHandler : IRequestHandler<ActualizarTarjetaCommand, TarjetaResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    public ActualizarTarjetaHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<TarjetaResponse> Handle(ActualizarTarjetaCommand command, CancellationToken cancellationToken)
    {
        var t = await _db.TarjetasCredito.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("TC_NO_ENCONTRADA",
                $"No se encontró la tarjeta '{command.Id}'.");
        if (t.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Tarjeta), t.Id);

        t.ActualizarDatos(command.NombreAlias, command.LimiteCreditoMxn, command.DiaCorte, command.DiaLimitePago);
        await _db.SaveChangesAsync(cancellationToken);
        return TarjetaMapper.ToResponse(t);
    }
}

// --------------------------------------------------- Bloquear / Reactivar / Cancelar

public sealed record BloquearTarjetaCommand(Guid Id, int VersionEsperada, string Motivo, DateOnly Fecha)
    : IRequest<TarjetaResponse>;

public sealed class BloquearTarjetaValidator : AbstractValidator<BloquearTarjetaCommand>
{
    public BloquearTarjetaValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class BloquearTarjetaHandler : IRequestHandler<BloquearTarjetaCommand, TarjetaResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    public BloquearTarjetaHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<TarjetaResponse> Handle(BloquearTarjetaCommand command, CancellationToken cancellationToken)
    {
        var t = await _db.TarjetasCredito.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("TC_NO_ENCONTRADA",
                $"No se encontró la tarjeta '{command.Id}'.");
        if (t.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Tarjeta), t.Id);

        t.Bloquear(command.Motivo, command.Fecha);
        await _db.SaveChangesAsync(cancellationToken);
        return TarjetaMapper.ToResponse(t);
    }
}

public sealed record ReactivarTarjetaCommand(Guid Id, int VersionEsperada) : IRequest<TarjetaResponse>;

public sealed class ReactivarTarjetaHandler : IRequestHandler<ReactivarTarjetaCommand, TarjetaResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    public ReactivarTarjetaHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<TarjetaResponse> Handle(ReactivarTarjetaCommand command, CancellationToken cancellationToken)
    {
        var t = await _db.TarjetasCredito.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("TC_NO_ENCONTRADA",
                $"No se encontró la tarjeta '{command.Id}'.");
        if (t.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Tarjeta), t.Id);

        t.Reactivar();
        await _db.SaveChangesAsync(cancellationToken);
        return TarjetaMapper.ToResponse(t);
    }
}

public sealed record CancelarTarjetaCommand(Guid Id, int VersionEsperada, DateOnly Fecha) : IRequest<TarjetaResponse>;

public sealed class CancelarTarjetaHandler : IRequestHandler<CancelarTarjetaCommand, TarjetaResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    public CancelarTarjetaHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<TarjetaResponse> Handle(CancelarTarjetaCommand command, CancellationToken cancellationToken)
    {
        var t = await _db.TarjetasCredito.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("TC_NO_ENCONTRADA",
                $"No se encontró la tarjeta '{command.Id}'.");
        if (t.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Tarjeta), t.Id);

        t.Cancelar(command.Fecha);
        await _db.SaveChangesAsync(cancellationToken);
        return TarjetaMapper.ToResponse(t);
    }
}

// --------------------------------------------------- Usuarios autorizados

public sealed record AgregarUsuarioAutorizadoCommand(
    Guid TarjetaId,
    int VersionEsperada,
    Guid EmpleadoId,
    DateOnly VigenciaDesde,
    DateOnly? VigenciaHasta,
    decimal? MontoMaxMensualMxn) : IRequest<UsuarioAutorizadoResponse>;

public sealed record UsuarioAutorizadoResponse(
    Guid Id,
    Guid TarjetaId,
    Guid EmpleadoId,
    DateOnly VigenciaDesde,
    DateOnly? VigenciaHasta,
    decimal? MontoMaxMensualMxn);

public sealed class AgregarUsuarioAutorizadoValidator : AbstractValidator<AgregarUsuarioAutorizadoCommand>
{
    public AgregarUsuarioAutorizadoValidator()
    {
        RuleFor(c => c.TarjetaId).NotEmpty();
        RuleFor(c => c.EmpleadoId).NotEmpty();
    }
}

public sealed class AgregarUsuarioAutorizadoHandler : IRequestHandler<AgregarUsuarioAutorizadoCommand, UsuarioAutorizadoResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    public AgregarUsuarioAutorizadoHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<UsuarioAutorizadoResponse> Handle(
        AgregarUsuarioAutorizadoCommand command, CancellationToken cancellationToken)
    {
        var t = await _db.TarjetasCredito
            .Include(x => x.UsuariosAutorizados)
            .FirstOrDefaultAsync(x => x.Id == command.TarjetaId, cancellationToken)
            ?? throw new EntityNotFoundException("TC_NO_ENCONTRADA",
                $"No se encontró la tarjeta '{command.TarjetaId}'.");
        if (t.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Tarjeta), t.Id);

        var usr = t.AgregarUsuarioAutorizado(
            command.EmpleadoId, command.VigenciaDesde, command.VigenciaHasta, command.MontoMaxMensualMxn);
        await _db.SaveChangesAsync(cancellationToken);
        return new UsuarioAutorizadoResponse(
            usr.Id, usr.TarjetaId, usr.EmpleadoId,
            usr.VigenciaDesde, usr.VigenciaHasta, usr.MontoMaxMensualMxn);
    }
}

public sealed record CerrarUsuarioAutorizadoCommand(
    Guid TarjetaId, int VersionEsperada, Guid UsuarioId, DateOnly Fecha) : IRequest;

public sealed class CerrarUsuarioAutorizadoHandler : IRequestHandler<CerrarUsuarioAutorizadoCommand>
{
    private readonly CuentasPorPagarDbContext _db;
    public CerrarUsuarioAutorizadoHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task Handle(CerrarUsuarioAutorizadoCommand command, CancellationToken cancellationToken)
    {
        var t = await _db.TarjetasCredito
            .Include(x => x.UsuariosAutorizados)
            .FirstOrDefaultAsync(x => x.Id == command.TarjetaId, cancellationToken)
            ?? throw new EntityNotFoundException("TC_NO_ENCONTRADA",
                $"No se encontró la tarjeta '{command.TarjetaId}'.");
        if (t.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Tarjeta), t.Id);

        t.CerrarUsuarioAutorizado(command.UsuarioId, command.Fecha);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

// --------------------------------------------------- Listar / Obtener

public sealed record ListarTarjetasQuery(
    EstadoTarjeta? Estado = null,
    Guid? TitularId = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<TarjetaResponse>>;

public sealed class ListarTarjetasHandler : IRequestHandler<ListarTarjetasQuery, PagedResponse<TarjetaResponse>>
{
    private readonly CuentasPorPagarDbContext _db;
    public ListarTarjetasHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<PagedResponse<TarjetaResponse>> Handle(
        ListarTarjetasQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.TarjetasCredito.AsNoTracking();
        if (query.Estado is EstadoTarjeta e) q = q.Where(t => t.Estado == e);
        if (query.TitularId is Guid tit) q = q.Where(t => t.TitularId == tit);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(t => t.Emisora).ThenBy(t => t.NombreAlias)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        return new PagedResponse<TarjetaResponse>(
            items.Select(TarjetaMapper.ToResponse).ToList(),
            offset, limit, total);
    }
}
