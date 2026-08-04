using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.AutorizarFactura;

/// <summary>
/// Autoriza manualmente una factura (override, permiso restringido
/// <c>cuentas_por_pagar.facturas.autorizar</c>). El flujo normal de
/// autorización (revisión → liberación → autorización) llega en F4.
/// Este comando existe en F3-PR2 para:
/// <list type="bullet">
///   <item>Disparar el <c>FacturaProveedorAutorizadaEvent</c> en
///         escenarios donde Compras / Contabilidad necesiten notificar
///         sin esperar al workflow.</item>
///   <item>Tests E2E del ciclo de eventos.</item>
/// </list>
/// </summary>
public sealed record AutorizarFacturaCommand(Guid Id, int VersionEsperada) : IRequest<AutorizarFacturaResponse>;

public sealed record AutorizarFacturaResponse(Guid Id, EstadoPasivo Estado, int Version);

public sealed class AutorizarFacturaValidator : AbstractValidator<AutorizarFacturaCommand>
{
    public AutorizarFacturaValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
    }
}

public sealed class AutorizarFacturaHandler : IRequestHandler<AutorizarFacturaCommand, AutorizarFacturaResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public AutorizarFacturaHandler(
        CuentasPorPagarDbContext db,
        ICurrentUserContext currentUser,
        IMediator mediator,
        IClock clock)
    {
        _db = db; _currentUser = currentUser; _mediator = mediator; _clock = clock;
    }

    public async Task<AutorizarFacturaResponse> Handle(AutorizarFacturaCommand command, CancellationToken cancellationToken)
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

        // Candado anti doble-pago (P7): las facturas nacidas de una
        // comprobación de caja chica o de una liquidación de viáticos
        // documentan gasto/IVA/DIOT de proveedores YA PAGADOS (en
        // efectivo por la caja o con el anticipo del empleado).
        // Autorizarlas mandaría a Tesorería un pasivo al proveedor y se
        // pagaría dos veces. El pasivo real (reposición de caja /
        // liquidación al empleado) se modela por separado — ver
        // docs/modulos/cuentas-por-pagar/12-pasivos-internos-tesoreria.md.
        var esDeCajaChica = await _db.ComprobacionesGastos
            .AsNoTracking()
            .AnyAsync(
                c => c.Tipo == Domain.ComprobacionGastos.TipoComprobacionGastos.ReembolsoCajaChica
                     && c.Lineas.Any(l => l.FacturaProveedorId == factura.Id),
                cancellationToken);
        var esDeViaticos = !esDeCajaChica && await _db.Set<Domain.Viaticos.LineaComprobacionViaticos>()
            .AsNoTracking()
            .AnyAsync(l => l.FacturaProveedorId == factura.Id, cancellationToken);
        if (esDeCajaChica || esDeViaticos)
        {
            throw new BusinessRuleException(
                "FACTURA_GASTO_INTERNO_NO_AUTORIZABLE",
                "Esta factura documenta un gasto de " +
                (esDeCajaChica ? "caja chica" : "viáticos") +
                " ya pagado — autorizarla generaría un pasivo duplicado al proveedor. " +
                "El pago correspondiente es la reposición de caja / liquidación al empleado.");
        }

        var ahora = _clock.UtcNow;
        factura.Autorizar(_currentUser.UserId, ahora);

        await _mediator.Publish(new FacturaProveedorAutorizadaDomainEvent(
            EmpresaId: factura.EmpresaId,
            FacturaProveedorId: factura.Id,
            OrdenCompraId: factura.OrdenCompraId,
            FechaAutorizacion: ahora), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new AutorizarFacturaResponse(factura.Id, factura.Estado, factura.Version);
    }
}
