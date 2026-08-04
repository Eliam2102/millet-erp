using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.AplicarNotaCredito;

public sealed record AplicarNotaCreditoAFacturaCommand(
    Guid FacturaId,
    int FacturaVersionEsperada,
    Guid NotaCreditoId,
    int NotaCreditoVersionEsperada,
    decimal Monto) : IRequest<AplicarNotaCreditoAFacturaResponse>;

public sealed record AplicarNotaCreditoAFacturaResponse(
    Guid FacturaId,
    decimal NcAplicadasTotal,
    decimal SaldoPendiente,
    Guid NotaCreditoId,
    EstadoNotaCredito EstadoNotaCredito,
    decimal SaldoPorAplicarNc);

public sealed class AplicarNotaCreditoAFacturaValidator : AbstractValidator<AplicarNotaCreditoAFacturaCommand>
{
    public AplicarNotaCreditoAFacturaValidator()
    {
        RuleFor(c => c.FacturaId).NotEmpty();
        RuleFor(c => c.NotaCreditoId).NotEmpty();
        RuleFor(c => c.Monto).GreaterThan(0);
    }
}

public sealed class AplicarNotaCreditoAFacturaHandler
    : IRequestHandler<AplicarNotaCreditoAFacturaCommand, AplicarNotaCreditoAFacturaResponse>
{
    private readonly CuentasPorPagarDbContext _db;

    public AplicarNotaCreditoAFacturaHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<AplicarNotaCreditoAFacturaResponse> Handle(
        AplicarNotaCreditoAFacturaCommand command, CancellationToken cancellationToken)
    {
        var factura = await _db.FacturasProveedor
            .FirstOrDefaultAsync(f => f.Id == command.FacturaId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "FACTURA_NO_ENCONTRADA",
                $"No se encontró la factura '{command.FacturaId}'.");
        if (factura.Version != command.FacturaVersionEsperada)
            throw new ConcurrencyException(nameof(FacturaProveedor), factura.Id);

        var nc = await _db.NotasCreditoProveedor
            .FirstOrDefaultAsync(n => n.Id == command.NotaCreditoId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "NC_NO_ENCONTRADA",
                $"No se encontró la NC '{command.NotaCreditoId}'.");
        if (nc.Version != command.NotaCreditoVersionEsperada)
            throw new ConcurrencyException(nameof(NotaCreditoProveedor), nc.Id);

        if (nc.ProveedorId != factura.ProveedorId)
        {
            throw new BusinessRuleException(
                "NC_FACTURA_PROVEEDOR_MISMATCH",
                "La NC y la factura pertenecen a proveedores distintos.");
        }

        if (nc.FacturaOrigenId != factura.Id)
        {
            throw new BusinessRuleException(
                "NC_FACTURA_NO_RELACIONADA",
                "La NC no está vinculada a esta factura como origen — vincular primero.");
        }

        // Las dos transiciones del agregado (factura y NC) son
        // atómicas dentro del mismo SaveChanges.
        nc.AplicarMonto(command.Monto);
        factura.AplicarNotaCredito(command.Monto);

        await _db.SaveChangesAsync(cancellationToken);

        return new AplicarNotaCreditoAFacturaResponse(
            FacturaId: factura.Id,
            NcAplicadasTotal: factura.NcAplicadasTotal,
            SaldoPendiente: factura.SaldoPendiente,
            NotaCreditoId: nc.Id,
            EstadoNotaCredito: nc.Estado,
            SaldoPorAplicarNc: nc.SaldoPorAplicar);
    }
}
