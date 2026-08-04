using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.Events;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.NotaCreditoProveedor.VincularFactura;

/// <summary>
/// Vincula manualmente una NC en estado <c>EnEspera</c> a su factura
/// origen — endpoint de respaldo cuando el Auxiliar conoce el match
/// antes que el worker, o cuando el UUID de relación del CFDI venía
/// errado y el worker no encontró match automático.
/// </summary>
public sealed record VincularFacturaNotaCreditoCommand(
    Guid Id,
    int VersionEsperada,
    Guid FacturaOrigenId) : IRequest<VincularFacturaNotaCreditoResponse>;

public sealed record VincularFacturaNotaCreditoResponse(
    Guid Id,
    EstadoNotaCredito Estado,
    Guid FacturaOrigenId,
    int Version);

public sealed class VincularFacturaNotaCreditoValidator : AbstractValidator<VincularFacturaNotaCreditoCommand>
{
    public VincularFacturaNotaCreditoValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.FacturaOrigenId).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
    }
}

public sealed class VincularFacturaNotaCreditoHandler
    : IRequestHandler<VincularFacturaNotaCreditoCommand, VincularFacturaNotaCreditoResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public VincularFacturaNotaCreditoHandler(CuentasPorPagarDbContext db, IMediator mediator, IClock clock)
    {
        _db = db; _mediator = mediator; _clock = clock;
    }

    public async Task<VincularFacturaNotaCreditoResponse> Handle(
        VincularFacturaNotaCreditoCommand command,
        CancellationToken cancellationToken)
    {
        var nc = await _db.NotasCreditoProveedor
            .FirstOrDefaultAsync(n => n.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "NC_NO_ENCONTRADA",
                $"No se encontró la NC con id '{command.Id}'.");

        if (nc.Version != command.VersionEsperada)
        {
            throw new ConcurrencyException(nameof(NotaCreditoProveedor), nc.Id);
        }

        var factura = await _db.FacturasProveedor
            .AsNoTracking()
            .Where(f => f.Id == command.FacturaOrigenId)
            .Select(f => new { f.Id, f.ProveedorId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(
                "FACTURA_ORIGEN_NO_ENCONTRADA",
                $"No se encontró la factura origen '{command.FacturaOrigenId}'.");

        if (factura.ProveedorId != nc.ProveedorId)
        {
            throw new BusinessRuleException(
                "NC_FACTURA_PROVEEDOR_MISMATCH",
                "La factura origen y la NC pertenecen a proveedores distintos.");
        }

        var ahora = _clock.UtcNow;
        nc.VincularFacturaOrigen(command.FacturaOrigenId, ahora);

        // Re-publicar el evento ahora que Compras puede ubicar la OC
        // vía la factura origen.
        await _mediator.Publish(new NotaCreditoProveedorRegistradaDomainEvent(
            EmpresaId: nc.EmpresaId,
            NotaCreditoId: nc.Id,
            ProveedorId: nc.ProveedorId,
            FacturaOrigenId: nc.FacturaOrigenId,
            TipoRelacionCfdi: (int)nc.TipoRelacionCfdi,
            Total: nc.Total,
            OcurridoEn: ahora), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new VincularFacturaNotaCreditoResponse(nc.Id, nc.Estado, nc.FacturaOrigenId!.Value, nc.Version);
    }
}
