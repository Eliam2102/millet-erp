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

    public ActualizarSerieHandler(CompartidoDbContext db) => _db = db;

    public async Task<SerieResponse> Handle(
        ActualizarSerieCommand command, CancellationToken cancellationToken)
    {
        var serie = await _db.Series
            .FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "SERIE_NO_ENCONTRADA",
                $"No existe serie con id '{command.Id}'.");

        serie.ActualizarDatos(
            prefijo: command.Prefijo,
            sufijo: command.Sufijo,
            reinicioPeriodo: command.ReinicioPeriodo,
            limpiarSufijo: command.LimpiarSufijo);

        await _db.SaveChangesAsync(cancellationToken);

        return CrearSerieHandler.Map(serie);
    }
}
