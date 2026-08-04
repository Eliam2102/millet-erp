using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Catalogos.Application.Monedas;

/// <summary>
/// Alta de moneda en <c>compartido.monedas</c> (F-Admin-PR5.1). UNIQUE
/// (Codigo) → 409 <c>MONEDA_CODIGO_DUPLICADO</c> si choca.
/// </summary>
public sealed record CrearMonedaCommand(
    string Codigo,
    string Nombre,
    int Decimales,
    bool Activa) : IRequest<MonedaResponse>;

public sealed class CrearMonedaValidator : AbstractValidator<CrearMonedaCommand>
{
    public CrearMonedaValidator()
    {
        RuleFor(c => c.Codigo).NotEmpty().Length(3).Matches("^[A-Za-z]{3}$");
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Decimales).InclusiveBetween(0, 6);
    }
}

public sealed class CrearMonedaHandler : IRequestHandler<CrearMonedaCommand, MonedaResponse>
{
    private readonly CompartidoDbContext _db;

    public CrearMonedaHandler(CompartidoDbContext db) => _db = db;

    public async Task<MonedaResponse> Handle(
        CrearMonedaCommand command, CancellationToken cancellationToken)
    {
        var codigo = command.Codigo.ToUpperInvariant();
        var existe = await _db.Monedas.AsNoTracking()
            .AnyAsync(m => m.Codigo == codigo, cancellationToken);
        if (existe)
        {
            throw new ConflictException(
                "MONEDA_CODIGO_DUPLICADO",
                $"Ya existe una moneda con código '{codigo}'.");
        }

        var moneda = new Moneda(
            Guid.CreateVersion7(), codigo, command.Nombre, command.Decimales, command.Activa);
        _db.Monedas.Add(moneda);
        await _db.SaveChangesAsync(cancellationToken);

        return new MonedaResponse(
            moneda.Id, moneda.Codigo, moneda.Nombre,
            moneda.Decimales, moneda.Activa, moneda.Version);
    }
}
