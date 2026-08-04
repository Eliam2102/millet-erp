using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Catalogos.Application.Monedas;

/// <summary>
/// Registra un tipo de cambio para una moneda en una fecha
/// (F-Admin-PR5.1). UNIQUE (MonedaId, Fecha) → 409
/// <c>TIPO_CAMBIO_DUPLICADO</c> si ya existe valor para ese par.
/// </summary>
public sealed record RegistrarTipoCambioCommand(
    Guid MonedaId,
    DateOnly Fecha,
    decimal ValorEnMxn,
    OrigenTipoCambio Origen = OrigenTipoCambio.Manual) : IRequest<TipoCambioResponse>;

public sealed class RegistrarTipoCambioValidator : AbstractValidator<RegistrarTipoCambioCommand>
{
    public RegistrarTipoCambioValidator()
    {
        RuleFor(c => c.MonedaId).NotEqual(Guid.Empty);
        RuleFor(c => c.ValorEnMxn).GreaterThan(0m);
        RuleFor(c => c.Origen).IsInEnum();
    }
}

public sealed class RegistrarTipoCambioHandler
    : IRequestHandler<RegistrarTipoCambioCommand, TipoCambioResponse>
{
    private readonly CompartidoDbContext _db;

    public RegistrarTipoCambioHandler(CompartidoDbContext db) => _db = db;

    public async Task<TipoCambioResponse> Handle(
        RegistrarTipoCambioCommand command, CancellationToken cancellationToken)
    {
        var existeMoneda = await _db.Monedas.AsNoTracking()
            .AnyAsync(m => m.Id == command.MonedaId, cancellationToken);
        if (!existeMoneda)
        {
            throw new EntityNotFoundException(
                "MONEDA_NO_ENCONTRADA",
                $"No existe moneda con id '{command.MonedaId}'.");
        }

        var duplicado = await _db.TiposCambio.AsNoTracking()
            .AnyAsync(t => t.MonedaId == command.MonedaId && t.Fecha == command.Fecha, cancellationToken);
        if (duplicado)
        {
            throw new ConflictException(
                "TIPO_CAMBIO_DUPLICADO",
                $"Ya existe tipo de cambio para moneda '{command.MonedaId}' en fecha '{command.Fecha:yyyy-MM-dd}'.");
        }

        var tc = new TipoCambio(
            Guid.CreateVersion7(),
            command.MonedaId,
            command.Fecha,
            command.ValorEnMxn,
            command.Origen);

        _db.TiposCambio.Add(tc);
        await _db.SaveChangesAsync(cancellationToken);

        return new TipoCambioResponse(
            tc.Id, tc.MonedaId, tc.Fecha, tc.ValorEnMxn, tc.Origen, tc.Version);
    }
}
