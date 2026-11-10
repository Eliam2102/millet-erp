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
using Millet.Facturacion.Domain.Facturas;
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
public sealed record AplicarAnticipoAFacturaCommand(Guid AnticipoId, Guid FacturaVentaId, decimal Importe, string? Comentarios = null)
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
        RuleFor(c => c.Comentarios).MaximumLength(NotaCredito.ComentariosMaxLength);
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
    IClock clock,
    ICatalogosSatReadPort catalogos)
    : IRequestHandler<AplicarAnticipoAFacturaCommand, AplicarAnticipoAFacturaResponse>
{

    public async Task<AplicarAnticipoAFacturaResponse> Handle(AplicarAnticipoAFacturaCommand command, CancellationToken cancellationToken)
    {
        var anticipo = await db.Anticipos.Include(a => a.Vinculaciones)
            .FirstOrDefaultAsync(a => a.Id == command.AnticipoId, cancellationToken)
            ?? throw new EntityNotFoundException("ANTICIPO_NO_ENCONTRADO", $"No existe el anticipo {command.AnticipoId}.");
        var factura = await db.FacturasVenta.Include(f => f.Relaciones).FirstOrDefaultAsync(f => f.Id == command.FacturaVentaId, cancellationToken)
            ?? throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", $"No existe la factura {command.FacturaVentaId}.");
        var facturaAnticipo = await db.FacturasAnticipo.FirstOrDefaultAsync(f => f.Id == anticipo.FacturaAnticipoId, cancellationToken)
            ?? throw new EntityNotFoundException("FACTURA_ANTICIPO_NO_ENCONTRADA", $"No existe el CFDI del anticipo {anticipo.Id}.");

        if (factura.Estado != EstadoTimbrado.Timbrado || string.IsNullOrWhiteSpace(factura.Uuid))
            throw new BusinessRuleException("FACTURA_NO_TIMBRADA",
                $"Solo se aplica un anticipo a una factura timbrada vigente (estado actual: {factura.Estado}).");
        if (facturaAnticipo.Estado != EstadoTimbrado.Timbrado || string.IsNullOrWhiteSpace(facturaAnticipo.Uuid))
            throw new BusinessRuleException("ANTICIPO_CFDI_NO_TIMBRADO",
                $"El CFDI del anticipo ({facturaAnticipo.Folio}) no está timbrado vigente (estado: {facturaAnticipo.Estado}).");
        // 86e3zg0vb: mismo cliente, no solo mismo RFC (XEXX es compartido por todos los clientes del extranjero).
        await AnticiposDelCliente.VerificarAsync(db, anticipo, facturaAnticipo, factura, cancellationToken);
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

        // M-173: una NC de aplicación de este anticipo a esta factura que quedó pendiente de timbrar se
        // resuelve en la bandeja (reintentar o descartar); otra NC duplicaría el CFDI.
        var pendiente = await db.NotasCredito.AsNoTracking()
            .Where(n => n.Motivo == MotivoNotaCredito.Amortizacion && n.AnticipoOrigenId == anticipo.Id
                && n.FacturaRelacionadaId == factura.Id
                && (n.Estado == EstadoTimbrado.TimbradoEnProceso || n.Estado == EstadoTimbrado.TimbradoFallido))
            .Select(n => n.Folio)
            .FirstOrDefaultAsync(cancellationToken);
        if (pendiente is not null)
            throw new BusinessRuleException("ANTICIPO_NC_PENDIENTE",
                $"La NC de aplicación {pendiente} de este anticipo está pendiente de timbrar; reinténtala o descártala en la bandeja.");

        // Sin saldos negativos: no se aplica más de lo que la factura tiene pendiente.
        var saldoFactura = await SaldoPendienteFactura.CalcularAsync(db, saldos, factura.Id, factura.Total, cancellationToken);
        if (command.Importe > saldoFactura)
            throw new BusinessRuleException("ANTICIPO_EXCEDE_SALDO_FACTURA",
                $"El importe ({command.Importe:N2}) excede el saldo pendiente de la factura ({saldoFactura:N2}).");

        var ahora = clock.UtcNow;
        // M11: período en la fecha fiscal de México, no en UTC.
        var fechaFiscal = Millet.Facturacion.Application.Timbrado.CfdiEmisionBuilder.FechaFiscal(ahora);
        if (!await periodo.EstaAbiertoAsync(fechaFiscal.Year, fechaFiscal.Month, cancellationToken))
            throw new BusinessRuleException("PERIODO_CERRADO",
                $"El período contable {fechaFiscal.Year}-{fechaFiscal.Month:D2} está cerrado; no se puede emitir.");

        var reserva = await sender.Send(new ReservarFolioCommand(factura.EmpresaId, factura.SucursalId,
            TipoDocumentoSerie.NotaCredito, Millet.Facturacion.Application.Timbrado.CfdiEmisionBuilder.DiaFiscal(ahora)), cancellationToken);
        // 86e3zg0vb: como las NC de aplicación de Millet — uso G02, forma 30, la tasa del anticipo y el TC del día de la NC.
        var receptor = NotaCredito.ReceptorAplicacion(new DatosFiscalesReceptor(factura.ReceptorRfc, factura.ReceptorNombre,
            factura.ReceptorRegimenFiscal, factura.ReceptorCodigoPostal, factura.ReceptorUsoCfdi, factura.ReceptorPais, factura.ReceptorEsGenerico));
        var tipoCambio = await TipoCambioFactura.DelDiaAsync(catalogos, factura.Moneda, ahora, cancellationToken);
        var nc = NotaCredito.CrearAmortizacion(factura.EmpresaId, reserva.Folio, reserva.Numero, factura.SucursalId,
            factura.CajaId, user.UserId, receptor, factura.SnapshotEmisor(), NotaCredito.FormaPagoAplicacionAnticipo, factura.Moneda,
            tipoCambio, fechaFiscal.Year, fechaFiscal.Month, factura.CanalVentaId, anticipo.Id, factura.Id,
            command.Importe, facturaAnticipo.TasaIvaParaAplicacion());
        if (factura.ComplementoCce is null && factura.ComportamientoFiscal == ComportamientoFiscal.ExportacionConCce)
            await db.Entry(factura).Reference(f => f.ComplementoCce).LoadAsync(cancellationToken);
        nc.EstablecerNumRegIdTribReceptor(factura.ComplementoCce?.ReceptorNumRegIdTrib ?? facturaAnticipo.ReceptorNumRegIdTrib);
        nc.FijarComentarios(command.Comentarios);
        // Millet (4-nov): la NC de aplicación relaciona solo la factura de venta; el UUID del anticipo va en la factura.
        nc.AgregarRelacion(factura.Uuid!, MotivoNotaCredito.Amortizacion.ClaveTipoRelacion());

        // M-173: el importe queda apartado para la factura y la NC se guarda en proceso antes del PAC.
        // Si no timbra, la NC queda pendiente en la bandeja con el importe apartado (sin reducir el
        // saldo): su reintento lo amortiza y descartarla lo libera.
        if (apartado is null)
            anticipo.Vincular(factura.Id, command.Importe, ahora);
        await TimbradoEjecutor.TimbrarYAplicarAsync(db, nc, CfdiEmisionBuilder.DesdeNotaCredito(nc, ahora),
            fiscal, cfdiRepo, ahora, cancellationToken,
            TimbradoDurable.AntesDelPac(db, nc));
        if (nc.Estado != EstadoTimbrado.Timbrado)
        {
            await db.SaveChangesAsync(cancellationToken);
            throw new BusinessRuleException("NC_AMORTIZACION_NO_TIMBRADA",
                $"La NC de aplicación del anticipo ({nc.Folio}) no se timbró ({nc.Estado}); queda pendiente de timbrar en la " +
                "bandeja con el importe apartado. Reinténtala o descártala desde ahí; no apliques el anticipo otra vez.");
        }

        anticipo.RegistrarAmortizacion(factura.Id, nc.Id, command.Importe, ahora);
        await eventos.PublishAsync(EventosContablesFacturacion.NotaCreditoTimbrada(
            nc, ahora, factura.Id, anticipo.Id, anticipo.ClienteId), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return new AplicarAnticipoAFacturaResponse(nc.Id, nc.Folio, nc.Uuid, nc.Total, anticipo.Saldo, saldoFactura - nc.Total);
    }
}
