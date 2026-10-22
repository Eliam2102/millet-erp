using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Facturas.EditarDatosCliente;

public sealed record EditarDatosClienteCommand(Guid Id, int VersionEsperada,
    string? OrdenCompraCliente, string? ReferenciaCliente, string? CondicionesPago,
    string? Proforma, string? Comentarios) : IRequest<int>;

public sealed class EditarDatosClienteValidator : AbstractValidator<EditarDatosClienteCommand>
{
    public EditarDatosClienteValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.OrdenCompraCliente).MaximumLength(60);
        RuleFor(c => c.ReferenciaCliente).MaximumLength(60);
        RuleFor(c => c.CondicionesPago).MaximumLength(60);
        RuleFor(c => c.Proforma).MaximumLength(40);
        RuleFor(c => c.Comentarios).MaximumLength(1000);
    }
}

public sealed class EditarDatosClienteHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance)
    : IRequestHandler<EditarDatosClienteCommand, int>
{
    public async Task<int> Handle(EditarDatosClienteCommand command, CancellationToken cancellationToken)
    {
        var scope = await alcance.ResolverAsync(cancellationToken);
        var factura = await scope.AplicarA(db.FacturasVenta)
            .FirstOrDefaultAsync(f => f.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", "La factura no existe o no está en tu alcance; selecciona otra factura.");
        if (factura.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(FacturaVenta), command.Id);
        factura.EditarDatosCliente(command.OrdenCompraCliente, command.ReferenciaCliente,
            command.CondicionesPago, command.Proforma, command.Comentarios);
        await db.SaveChangesAsync(cancellationToken);
        return factura.Version;
    }
}
