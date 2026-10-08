using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Series;

/// <summary>
/// PATCH parcial de una <see cref="Serie"/> (F-Admin-PR6.1). Convención
/// del repo: <c>null</c> = no tocar; <see cref="LimpiarSufijo"/> = true
/// setea Sufijo a null. Inmutables: <see cref="CrearSerieCommand.EmpresaId"/>,
/// <see cref="CrearSerieCommand.SucursalId"/> y
/// <see cref="CrearSerieCommand.TipoDocumento"/> (parte de la business
/// key).
/// </summary>
public sealed record ActualizarSerieCommand(
    Guid Id,
    int VersionEsperada,
    string? Prefijo,
    string? Sufijo,
    ReinicioPeriodo? ReinicioPeriodo,
    bool LimpiarSufijo) : IRequest<SerieResponse>;

public sealed class ActualizarSerieValidator : AbstractValidator<ActualizarSerieCommand>
{
    public ActualizarSerieValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Prefijo!).NotEmpty().MaximumLength(10)
            .When(c => c.Prefijo is not null);
        RuleFor(c => c.Sufijo!).MaximumLength(10)
            .When(c => c.Sufijo is not null);
        RuleFor(c => c.ReinicioPeriodo!.Value).IsInEnum()
            .When(c => c.ReinicioPeriodo is not null);
    }
}

public sealed class ActualizarSerieHandler
    : IRequestHandler<ActualizarSerieCommand, SerieResponse>
{
    private readonly CompartidoDbContext _db;
    private readonly SerieSucursalScope _scope;

    public ActualizarSerieHandler(CompartidoDbContext db, SerieSucursalScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<SerieResponse> Handle(
        ActualizarSerieCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var serie = await _db.Series.FromSqlInterpolated(
            $"SELECT * FROM compartido.series WHERE id = {command.Id} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(
                "SERIE_NO_ENCONTRADA",
                $"No existe serie con id '{command.Id}'.");

        await _scope.VerificarAsync(serie.SucursalId, cancellationToken);

        if (serie.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Serie), serie.Id);

        if (Serie.EsFiscal(serie.TipoDocumento))
        {
            var cambia = (command.Prefijo is not null && command.Prefijo != serie.Prefijo)
                || (command.Sufijo is not null && command.Sufijo != serie.Sufijo)
                || (command.LimpiarSufijo && serie.Sufijo is not null)
                || (command.ReinicioPeriodo is not null && command.ReinicioPeriodo != serie.ReinicioPeriodo);
            if (cambia && await _db.SecuenciasFolio.AnyAsync(s => s.SerieId == serie.Id, cancellationToken))
                throw new BusinessRuleException("SERIE_USADA_INMUTABLE", "La serie fiscal ya fue utilizada. Desactívala y crea otra sin alterar su historia.");
            if (command.ReinicioPeriodo is not null && command.ReinicioPeriodo != ReinicioPeriodo.None)
                throw new BusinessRuleException("SERIE_FISCAL_SIN_REINICIO", "Las series fiscales mantienen continuidad sin reinicio de periodo.");
        }

        serie.ActualizarDatos(
            prefijo: command.Prefijo,
            sufijo: command.Sufijo,
            reinicioPeriodo: command.ReinicioPeriodo,
            limpiarSufijo: command.LimpiarSufijo);

        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return CrearSerieHandler.Map(serie);
    }
}
