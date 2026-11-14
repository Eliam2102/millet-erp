using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cancelaciones;

/// <summary>
/// Reglas que se revisan al solicitar y otra vez al autorizar una cancelación: el
/// comprobante sigue timbrado y no rompe la cadena de un anticipo (invariante 8).
/// </summary>
internal static class CadenaCancelacion
{
    public static async Task<Comprobante> CargarCancelableAsync(
        FacturacionDbContext db, Guid comprobanteId, CancellationToken cancellationToken)
    {
        var comprobante = await db.Comprobantes
            .FirstOrDefaultAsync(c => c.Id == comprobanteId, cancellationToken)
            ?? throw new EntityNotFoundException("COMPROBANTE_NO_ENCONTRADO", $"No existe el comprobante {comprobanteId}.");

        // P45: un CFDI timbrado fuera del ERP se cancela donde se emitió; aquí ni se solicita ni se manda al PAC.
        if (comprobante.EsOrigenExterno)
            throw new BusinessRuleException(
                "COMPROBANTE_EXTERNO",
                $"El comprobante {comprobante.Folio} se timbró fuera del ERP; su cancelación se hace en el sistema que lo emitió.");

        if (comprobante.Estado != EstadoTimbrado.Timbrado)
            throw new BusinessRuleException(
                "COMPROBANTE_NO_CANCELABLE",
                $"Solo un comprobante Timbrado puede cancelarse (estado actual: {comprobante.Estado}).");

        await ValidarAnticipoAsync(db, comprobante, cancellationToken);
        return comprobante;
    }

    /// <summary>
    /// Si el comprobante es una factura de anticipo, bloquea la cancelación cuando
    /// el anticipo tiene amortizaciones cuyas NCs siguen vigentes (invariante 8).
    /// </summary>
    private static async Task ValidarAnticipoAsync(FacturacionDbContext db, Comprobante comprobante, CancellationToken cancellationToken)
    {
        if (comprobante is not FacturaAnticipo) return;

        var anticipo = await db.Anticipos
            .Include(a => a.Vinculaciones)
            .FirstOrDefaultAsync(a => a.FacturaAnticipoId == comprobante.Id, cancellationToken);
        if (anticipo is null) return;

        var ncIds = anticipo.Vinculaciones
            .Where(v => v.NcAmortizacionId is not null)
            .Select(v => v.NcAmortizacionId!.Value)
            .ToList();
        if (ncIds.Count > 0 && await db.NotasCredito
                .AnyAsync(n => ncIds.Contains(n.Id) && n.Estado != EstadoTimbrado.Cancelado, cancellationToken))
            throw new BusinessRuleException(
                "ANTICIPO_AMORTIZADO_NCS_VIGENTES",
                "No se puede cancelar el anticipo: primero cancela las NC de amortización relacionadas (invariante 8).");

        // F6.3: la NC de devolución en dinero también relaciona el CFDI del anticipo (relación 01).
        var hayDevolucionVigente = await db.NotasCredito.AnyAsync(n =>
            n.AnticipoOrigenId == anticipo.Id
            && n.Motivo == MotivoNotaCredito.DevolucionAnticipo
            && n.Estado != EstadoTimbrado.Cancelado, cancellationToken);
        if (hayDevolucionVigente)
            throw new BusinessRuleException(
                "ANTICIPO_DEVUELTO_NCS_VIGENTES",
                "No se puede cancelar el anticipo: primero cancela las NC de devolución del anticipo.");
    }

    /// <summary>La solicitud pendiente de autorización del comprobante.</summary>
    public static async Task<SolicitudCancelacion> PendienteDeAsync(
        FacturacionDbContext db, Guid comprobanteId, CancellationToken cancellationToken) =>
        await db.SolicitudesCancelacion
            .Where(s => s.ComprobanteId == comprobanteId && s.Estado == EstadoSolicitudCancelacion.PendienteAutorizacion)
            .OrderByDescending(s => s.SolicitadaEn)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new EntityNotFoundException(
            "CANCELACION_PENDIENTE_NO_ENCONTRADA", $"El comprobante {comprobanteId} no tiene una cancelación pendiente de autorización.");
}
