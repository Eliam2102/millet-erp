using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.AplicarAnticipo;

public sealed record AplicarAnticipoAFacturaCommand(
    Guid FacturaId,
    int FacturaVersionEsperada,
    Guid AnticipoId,
    int AnticipoVersionEsperada,
    decimal Monto) : IRequest<AplicarAnticipoAFacturaResponse>;

public sealed record AplicarAnticipoAFacturaResponse(
    Guid FacturaId,
    decimal AnticipoAplicadoTotal,
    decimal SaldoPendiente,
    Guid AnticipoId,
    EstadoAnticipo EstadoAnticipo,
    decimal SaldoAmortizableAnticipo);

public sealed class AplicarAnticipoAFacturaValidator : AbstractValidator<AplicarAnticipoAFacturaCommand>
{
    public AplicarAnticipoAFacturaValidator()
    {
        RuleFor(c => c.FacturaId).NotEmpty();
        RuleFor(c => c.AnticipoId).NotEmpty();
        RuleFor(c => c.Monto).GreaterThan(0);
    }
}

public sealed class AplicarAnticipoAFacturaHandler
    : IRequestHandler<AplicarAnticipoAFacturaCommand, AplicarAnticipoAFacturaResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;
    private readonly Integration.Mappers.PasivoAutorizadoParaPagoMapper _pasivos;

    public AplicarAnticipoAFacturaHandler(CuentasPorPagarDbContext db, IClock clock, Integration.Mappers.PasivoAutorizadoParaPagoMapper pasivos)
    {
        _db = db; _clock = clock; _pasivos = pasivos;
    }

    public async Task<AplicarAnticipoAFacturaResponse> Handle(
        AplicarAnticipoAFacturaCommand command, CancellationToken cancellationToken)
    {
        var factura = await _db.FacturasProveedor
            .FirstOrDefaultAsync(f => f.Id == command.FacturaId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "FACTURA_NO_ENCONTRADA",
                $"No se encontró la factura '{command.FacturaId}'.");
        if (factura.Version != command.FacturaVersionEsperada)
            throw new ConcurrencyException(nameof(FacturaProveedor), factura.Id);

        var anticipo = await _db.AnticiposProveedor
            .FirstOrDefaultAsync(a => a.Id == command.AnticipoId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ANTICIPO_NO_ENCONTRADO",
                $"No se encontró el anticipo '{command.AnticipoId}'.");
        if (anticipo.Version != command.AnticipoVersionEsperada)
            throw new ConcurrencyException(nameof(AnticipoProveedor), anticipo.Id);

        if (anticipo.ProveedorId != factura.ProveedorId)
        {
            throw new BusinessRuleException(
                "ANTICIPO_FACTURA_PROVEEDOR_MISMATCH",
                "El anticipo y la factura pertenecen a proveedores distintos.");
        }

        if (anticipo.Moneda != factura.Moneda)
            throw new BusinessRuleException("ANTICIPO_MONEDA_DISTINTA", "El anticipo y la factura deben tener la misma moneda.");
        var ahora = _clock.UtcNow;
        anticipo.Amortizar(command.Monto, ahora);
        factura.AplicarAnticipo(command.Monto, DateOnly.FromDateTime(ahora.UtcDateTime), anticipo.Id);

        if (factura.Estado == EstadoPasivo.Autorizada)
            await _pasivos.Handle(new Domain.FacturaProveedor.Events.FacturaProveedorAutorizadaDomainEvent(
                factura.EmpresaId, factura.Id, factura.OrdenCompraId, ahora), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return new AplicarAnticipoAFacturaResponse(
            FacturaId: factura.Id,
            AnticipoAplicadoTotal: factura.AnticipoAplicadoTotal,
            SaldoPendiente: factura.SaldoPendiente,
            AnticipoId: anticipo.Id,
            EstadoAnticipo: anticipo.Estado,
            SaldoAmortizableAnticipo: anticipo.SaldoAmortizable);
    }
}
