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
    private readonly Integration.Mappers.FacturaPagoAplicadoMapper _pagosOc;
    private readonly CuentasPorPagarDbContext _db;
    private readonly Integration.Mappers.PasivoAutorizadoParaPagoMapper _pasivos;
    private readonly Millet.SharedKernel.Application.IClock _clock;

    public AplicarNotaCreditoAFacturaHandler(CuentasPorPagarDbContext db, Integration.Mappers.PasivoAutorizadoParaPagoMapper pasivos, Millet.SharedKernel.Application.IClock clock, Integration.Mappers.FacturaPagoAplicadoMapper pagosOc)
    {
        _pagosOc = pagosOc; _db = db; _pasivos = pasivos; _clock = clock; }

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
        if (nc.Moneda != factura.Moneda)
            throw new BusinessRuleException("NC_MONEDA_DISTINTA", "La NC y la factura deben tener la misma moneda.");
        var cargo = await _db.NotasCargo.AsNoTracking().FirstOrDefaultAsync(
            n => n.NotaCreditoProveedorId == nc.Id && n.FacturaOrigenId == factura.Id, cancellationToken);
        // La NC 03 solo se aplica sustituyendo a su nota de cargo (formalización); a mano descontaría dos veces la devolución.
        if (nc.TipoRelacionCfdi == TipoRelacionCfdi.Devolucion && cargo is null)
            throw new BusinessRuleException("NC03_SIN_NOTA_CARGO", "La NC de devolución (tipo 03) se aplica al formalizar su nota de cargo; no puede aplicarse manualmente sin ella.");
        // La NC fiscal formaliza el cargo ya aplicado; no descuenta dos veces el mismo pasivo.
        var cargoAplicado = cargo is null ? 0 : await _db.MovimientosPasivo
            .Where(m => m.FacturaProveedorId == factura.Id && m.DocumentoId == cargo.Id && m.Tipo == TipoMovimientoPasivo.NotaCargo)
            .SumAsync(m => m.Monto, cancellationToken);
        var reconocido = cargoAplicado == 0 ? 0 :
            Math.Min(nc.MontoAplicado + command.Monto, cargoAplicado) - Math.Min(nc.MontoAplicado, cargoAplicado);
        nc.AplicarMonto(command.Monto);
        if (command.Monto > reconocido)
            factura.AplicarNotaCredito(command.Monto - reconocido, DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime), nc.Id);

        if (factura.Estado == EstadoPasivo.Autorizada)
            await _pasivos.Handle(new Domain.FacturaProveedor.Events.FacturaProveedorAutorizadaDomainEvent(
                factura.EmpresaId, factura.Id, factura.OrdenCompraId, _clock.UtcNow), cancellationToken);
        var aplicacion = factura.Movimientos.LastOrDefault();
        if (command.Monto > reconocido && aplicacion is not null)
            await _pagosOc.Handle(new Domain.FacturaProveedor.Events.FacturaProveedorPagoAplicadoDomainEvent(
                factura.EmpresaId, factura.Id, factura.OrdenCompraId, factura.ImporteSaldado, _clock.UtcNow, aplicacion.Id), cancellationToken);
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
