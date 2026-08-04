using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Catalogos.Application.Monedas;

/// <summary>
/// PATCH parcial sobre Moneda (F-Admin-PR5.1). Inmutables:
/// <c>Id</c> y <c>Codigo</c> (business key).
/// </summary>
public sealed record ActualizarMonedaCommand(
    Guid Id,
    string? Nombre,
    int? Decimales,
    bool? Activa) : IRequest<MonedaResponse>;

public sealed class ActualizarMonedaValidator : AbstractValidator<ActualizarMonedaCommand>
{
    public ActualizarMonedaValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Nombre!).NotEmpty().MaximumLength(100)
            .When(c => c.Nombre is not null);
        RuleFor(c => c.Decimales!.Value).InclusiveBetween(0, 6)
            .When(c => c.Decimales.HasValue);
    }
}

public sealed class ActualizarMonedaHandler
    : IRequestHandler<ActualizarMonedaCommand, MonedaResponse>
{
    private readonly CompartidoDbContext _db;

    public ActualizarMonedaHandler(CompartidoDbContext db) => _db = db;

    public async Task<MonedaResponse> Handle(
        ActualizarMonedaCommand command, CancellationToken cancellationToken)
    {
        var moneda = await _db.Monedas
            .FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "MONEDA_NO_ENCONTRADA",
                $"No existe moneda con id '{command.Id}'.");

        moneda.ActualizarDatos(
            nombre: command.Nombre,
            decimales: command.Decimales,
            activa: command.Activa);
        await _db.SaveChangesAsync(cancellationToken);

        return new MonedaResponse(
            moneda.Id, moneda.Codigo, moneda.Nombre,
            moneda.Decimales, moneda.Activa, moneda.Version);
    }
}
