using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Administracion.Domain;
using Millet.Facturacion.Application.Facturas;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Anticipos.AplicarAnticipoAFactura;

/// <summary>
/// F1-CXC-05 Parte 2 (compensación): aplica el saldo de un anticipo a una factura del mismo cliente que ya está timbrada.
/// Emite y timbra la NC de amortización (relación 07 al CFDI del anticipo y a la factura), igual que al emitir la factura
/// con anticipo, y baja el saldo del anticipo. El evento de NC timbrada lleva la factura: CxC la descuenta de su saldo,
/// con el movimiento ligado a la NC (rastro anticipo → NC → factura). Si el anticipo ya tenía apartado un importe para esa
/// factura (vincular), este paso lo convierte en aplicación por ese mismo importe.
/// </summary>
public sealed record AplicarAnticipoAFacturaCommand(Guid AnticipoId, Guid FacturaVentaId, decimal Importe)
    : IRequest<AplicarAnticipoAFacturaResponse>;

public sealed record AplicarAnticipoAFacturaResponse(
    Guid NotaCreditoId, string Folio, string? Uuid, decimal Importe, decimal SaldoAnticipo, decimal SaldoFactura);

public sealed class AplicarAnticipoAFacturaValidator : AbstractValidator<AplicarAnticipoAFacturaCommand>
{
    public AplicarAnticipoAFacturaValidator()
    {
        RuleFor(c => c.AnticipoId).NotEmpty();
        RuleFor(c => c.FacturaVentaId).NotEmpty();
        RuleFor(c => c.Importe).GreaterThan(0);
    }
}

public sealed class AplicarAnticipoAFacturaHandler(
    FacturacionDbContext db,
    ISender sender,
    IPeriodoContablePort periodo,
    ISaldoFacturaReadPort saldos,
    ICfdiTimbradoPort fiscal,
    ICfdiRepositorioPort cfdiRepo,
    IIntegrationEventPublisher eventos,
    ICurrentUserContext user,
    IClock clock)
    : IRequestHandler<AplicarAnticipoAFacturaCommand, AplicarAnticipoAFacturaResponse>
{
    private const decimal TasaIvaEstandar = 0.16m;

    public async Task<AplicarAnticipoAFacturaResponse> Handle(AplicarAnticipoAFacturaCommand command, CancellationToken cancellationToken)
    {
        var anticipo = await db.Anticipos.Include(a => a.Vinculaciones)
            .FirstOrDefaultAsync(a => a.Id == command.AnticipoId, cancellationToken)
            ?? throw new EntityNotFoundException("ANTICIPO_NO_ENCONTRADO", $"No existe el anticipo {command.AnticipoId}.");
        var factura = await db.FacturasVenta.FirstOrDefaultAsync(f => f.Id == command.FacturaVentaId, cancellationToken)
            ?? throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", $"No existe la factura {command.FacturaVentaId}.");
        var facturaAnticipo = await db.FacturasAnticipo.FirstOrDefaultAsync(f => f.Id == anticipo.FacturaAnticipoId, cancellationToken)
            ?? throw new EntityNotFoundException("FACTURA_ANTICIPO_NO_ENCONTRADA", $"No existe el CFDI del anticipo {anticipo.Id}.");

        if (factura.Estado != EstadoTimbrado.Timbrado || string.IsNullOrWhiteSpace(factura.Uuid))
            throw new BusinessRuleException("FACTURA_NO_TIMBRADA",
                $"Solo se aplica un anticipo a una factura timbrada vigente (estado actual: {factura.Estado}).");
        if (facturaAnticipo.Estado != EstadoTimbrado.Timbrado || string.IsNullOrWhiteSpace(facturaAnticipo.Uuid))
            throw new BusinessRuleException("ANTICIPO_CFDI_NO_TIMBRADO",
                $"El CFDI del anticipo ({facturaAnticipo.Folio}) no está timbrado vigente (estado: {facturaAnticipo.Estado}).");
        if (!string.Equals(anticipo.ReceptorRfc, factura.ReceptorRfc, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("ANTICIPO_CLIENTE_DISTINTO",
                "El anticipo y la factura deben ser del mismo cliente (RFC receptor).");
        if (!string.Equals(anticipo.Moneda, factura.Moneda, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("ANTICIPO_MONEDA_DISTINTA",
                $"El anticipo está en {anticipo.Moneda} y la factura en {factura.Moneda}.");

        // Un importe ya apartado (vincular) para esta factura manda: se aplica completo, sin mezclar otro importe.
        var apartado = anticipo.Vinculaciones.FirstOrDefault(v => v.FacturaVentaId == factura.Id && v.NcAmortizacionId is null);
        if (apartado is not null && apartado.Importe != command.Importe)
            throw new BusinessRuleException("ANTICIPO_IMPORTE_APARTADO",
                $"El anticipo ya tiene apartados {apartado.Importe:N2} para esta factura; aplica ese importe.");
        if (apartado is null && command.Importe > anticipo.SaldoDisponible)
            throw new BusinessRuleException("ANTICIPO_SALDO_INSUFICIENTE",
                $"El importe ({command.Importe:N2}) excede el saldo disponible del anticipo ({anticipo.SaldoDisponible:N2}).");

        // Sin saldos negativos: no se aplica más de lo que la factura tiene pendiente.
        var saldoFactura = await SaldoPendienteFactura.CalcularAsync(db, saldos, factura.Id, factura.Total, cancellationToken);
        if (command.Importe > saldoFactura)
            throw new BusinessRuleException("ANTICIPO_EXCEDE_SALDO_FACTURA",
                $"El importe ({command.Importe:N2}) excede el saldo pendiente de la factura ({saldoFactura:N2}).");

        var ahora = clock.UtcNow;
        if (!await periodo.EstaAbiertoAsync(ahora.Year, ahora.Month, cancellationToken))
            throw new BusinessRuleException("PERIODO_CERRADO",
                $"El período contable {ahora.Year}-{ahora.Month:D2} está cerrado; no se puede emitir.");

        var reserva = await sender.Send(new ReservarFolioCommand(factura.EmpresaId, factura.SucursalId,
            TipoDocumentoSerie.NotaCredito, DateOnly.FromDateTime(ahora.UtcDateTime)), cancellationToken);
        var receptor = new DatosFiscalesReceptor(factura.ReceptorRfc, factura.ReceptorNombre, factura.ReceptorRegimenFiscal,
            factura.ReceptorCodigoPostal, factura.ReceptorUsoCfdi, factura.ReceptorPais, factura.ReceptorEsGenerico);
        var nc = NotaCredito.CrearAmortizacion(factura.EmpresaId, reserva.Folio, reserva.Numero, factura.SucursalId,
            factura.CajaId, user.UserId, receptor, factura.SnapshotEmisor(), factura.FormaPago, factura.Moneda,
            factura.TipoCambio, ahora.Year, ahora.Month, factura.CanalVentaId, anticipo.Id, factura.Id,
            command.Importe, TasaIvaEstandar);
        // Millet (4-nov): la NC de aplicación relaciona solo la factura de venta; el UUID del anticipo va en la factura.
        nc.AgregarRelacion(factura.Uuid!, MotivoNotaCredito.Amortizacion.ClaveTipoRelacion());

        await TimbradoEjecutor.TimbrarYAplicarAsync(db, nc, CfdiEmisionBuilder.DesdeNotaCredito(nc, ahora),
            fiscal, cfdiRepo, ahora, cancellationToken);
        // Sin timbre no hay aplicación: no se toca el saldo del anticipo ni se publica nada (rollback).
        if (nc.Estado != EstadoTimbrado.Timbrado)
            throw new BusinessRuleException("NC_AMORTIZACION_NO_TIMBRADA",
                $"La NC de aplicación del anticipo no se timbró ({nc.Estado}); no se aplicó el anticipo. Intenta de nuevo.");

        anticipo.RegistrarAmortizacion(factura.Id, nc.Id, command.Importe, ahora);
        db.NotasCredito.Add(nc);
        await eventos.PublishAsync(EventosContablesFacturacion.NotaCreditoTimbrada(
            nc, ahora, factura.Id, anticipo.Id, anticipo.ClienteId), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return new AplicarAnticipoAFacturaResponse(nc.Id, nc.Folio, nc.Uuid, nc.Total, anticipo.Saldo, saldoFactura - nc.Total);
    }
}
