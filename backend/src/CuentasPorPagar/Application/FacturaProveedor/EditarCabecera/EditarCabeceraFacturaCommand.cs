using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.EditarCabecera;

public sealed record EditarCabeceraFacturaCommand(
    Guid Id,
    int VersionEsperada,
    string? FolioProveedor,
    string? SerieProveedor,
    DateOnly FechaVencimiento,
    DateTimeOffset FechaContabilizacion) : IRequest<EditarCabeceraFacturaResponse>;

public sealed record EditarCabeceraFacturaResponse(Guid Id, int Version);

public sealed class EditarCabeceraFacturaValidator : AbstractValidator<EditarCabeceraFacturaCommand>
{
    public EditarCabeceraFacturaValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
        RuleFor(c => c.FolioProveedor).MaximumLength(40);
        RuleFor(c => c.SerieProveedor).MaximumLength(25);
    }
}

public sealed class EditarCabeceraFacturaHandler : IRequestHandler<EditarCabeceraFacturaCommand, EditarCabeceraFacturaResponse>
{
    private readonly CuentasPorPagarDbContext _db;

    public EditarCabeceraFacturaHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<EditarCabeceraFacturaResponse> Handle(
        EditarCabeceraFacturaCommand command,
        CancellationToken cancellationToken)
    {
        var factura = await _db.FacturasProveedor
            .FirstOrDefaultAsync(f => f.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "FACTURA_NO_ENCONTRADA",
                $"No se encontró la factura con id '{command.Id}'.");

        if (factura.Version != command.VersionEsperada)
        {
            throw new ConcurrencyException(nameof(FacturaProveedor), factura.Id);
        }

        factura.EditarCabeceraPreAutorizacion(
            command.FolioProveedor, command.SerieProveedor,
            command.FechaVencimiento, command.FechaContabilizacion);

        await _db.SaveChangesAsync(cancellationToken);
        return new EditarCabeceraFacturaResponse(factura.Id, factura.Version);
    }
}
