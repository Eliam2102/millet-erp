using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Conciliacion;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;

namespace Millet.CuentasPorCobrar.Application.Conciliacion;

/// <summary>Factura de la proyección de cartera, reducida a lo que se concilia.</summary>
public sealed record FacturaEnCartera(Guid Id, Guid FacturaVentaId, decimal Total, EstadoFacturaCartera Estado);

/// <summary>Movimiento de la proyección de cartera, reducido a lo que se concilia.</summary>
public sealed record MovimientoEnCartera(Guid FacturaCarteraId, TipoMovimientoCartera Tipo, Guid OrigenComprobanteId, decimal Importe, bool Revertido);

/// <summary>
/// C7.7: compara lo que registró Facturación en el día contra la proyección de
/// cartera y devuelve cada diferencia con su causa probable. Función pura: no lee
/// la base ni el reloj.
/// </summary>
public static class ComparadorConciliacion
{
    public static IReadOnlyList<DiferenciaCalculada> Comparar(
        ComprobantesDelDia facturacion,
        IReadOnlyCollection<FacturaEnCartera> facturasCartera,
        IReadOnlyCollection<MovimientoEnCartera> movimientosCartera)
    {
        var cartera = facturasCartera.ToDictionary(f => f.FacturaVentaId);
        var diferencias = new List<DiferenciaCalculada>();

        foreach (var f in facturacion.Facturas)
        {
            cartera.TryGetValue(f.FacturaVentaId, out var enCartera);
            DiferenciaCalculada Diferencia(TipoDiferenciaConciliacion tipo, string causa) => new(
                tipo, f.FacturaVentaId, "Factura", f.Folio, f.Uuid, f.FacturaVentaId,
                f.ReceptorRfc, f.ReceptorNombre, f.Total, enCartera?.Total, causa);

            if (f.Cancelada)
            {
                if (enCartera is not null && enCartera.Estado != EstadoFacturaCartera.Cancelada)
                    diferencias.Add(Diferencia(TipoDiferenciaConciliacion.CancelacionPendiente,
                        "La factura está cancelada en Facturación y sigue vigente en cartera: no se procesó el evento de cancelación."));
                continue;
            }

            if (enCartera is null)
                diferencias.Add(Diferencia(TipoDiferenciaConciliacion.FacturaNoEnCartera,
                    "La factura está timbrada y no está en cartera: no se procesó el evento de factura timbrada."));
            else if (enCartera.Estado == EstadoFacturaCartera.Cancelada)
                diferencias.Add(Diferencia(TipoDiferenciaConciliacion.CanceladoSoloEnCartera,
                    "La cartera tiene la factura cancelada y en Facturación está vigente."));
            else if (enCartera.Total != f.Total)
                diferencias.Add(Diferencia(TipoDiferenciaConciliacion.ImporteDistinto,
                    "El total de la factura en cartera es distinto al de Facturación."));
        }

        foreach (var a in facturacion.Aplicaciones)
        {
            var (tipoMovimiento, tipoComprobante, faltante, nombre) = a.Tipo switch
            {
                TipoAplicacionFacturacion.NotaCredito => (TipoMovimientoCartera.NotaCredito, "Nota de crédito",
                    TipoDiferenciaConciliacion.NotaCreditoNoReflejada, "la nota de crédito"),
                TipoAplicacionFacturacion.ReciboPago => (TipoMovimientoCartera.Pago, "REP",
                    TipoDiferenciaConciliacion.PagoNoReflejado, "el complemento de pago"),
                _ => (TipoMovimientoCartera.Pago, "Cobro de mostrador",
                    TipoDiferenciaConciliacion.PagoNoReflejado, "el cobro de mostrador"),
            };

            cartera.TryGetValue(a.FacturaVentaId, out var factura);
            var movimiento = factura is null
                ? null
                : movimientosCartera.FirstOrDefault(m => m.OrigenComprobanteId == a.OrigenId
                    && m.FacturaCarteraId == factura.Id && m.Tipo == tipoMovimiento);

            DiferenciaCalculada Diferencia(TipoDiferenciaConciliacion tipo, string causa) => new(
                tipo, a.OrigenId, tipoComprobante, a.Folio, a.Uuid, a.FacturaVentaId,
                a.ReceptorRfc, a.ReceptorNombre, a.Importe, movimiento?.Importe, causa);

            if (a.Cancelada)
            {
                if (movimiento is { Revertido: false })
                    diferencias.Add(Diferencia(TipoDiferenciaConciliacion.CancelacionPendiente,
                        $"Facturación canceló {nombre} y el movimiento sigue aplicado en cartera: no se procesó el evento de cancelación."));
                continue;
            }

            if (movimiento is null)
                diferencias.Add(Diferencia(faltante, factura is null
                    ? $"La factura de {nombre} no está en cartera, así que el movimiento no se pudo aplicar."
                    : $"No se procesó el evento de {nombre}: la factura no tiene el movimiento en cartera."));
            else if (movimiento.Revertido)
                diferencias.Add(Diferencia(TipoDiferenciaConciliacion.CanceladoSoloEnCartera,
                    $"La cartera revirtió el movimiento y {nombre} sigue vigente en Facturación."));
            else if (movimiento.Importe != a.Importe)
                diferencias.Add(Diferencia(TipoDiferenciaConciliacion.ImporteDistinto,
                    $"El importe aplicado en cartera es distinto al de {nombre}."));
        }

        return diferencias;
    }
}
