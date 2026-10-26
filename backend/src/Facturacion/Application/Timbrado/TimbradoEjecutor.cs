using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;

namespace Millet.Facturacion.Application.Timbrado;

/// <summary>
/// Ejecuta el timbrado de un <see cref="Comprobante"/> y aplica el resultado
/// (F12-PR1 — generaliza el <c>CartaPorteTimbradoHelper</c> de F8 a todos los
/// tipos): FSM <c>Borrador → TimbradoEnProceso → Timbrado | TimbradoFallido</c>
/// + persistencia del XML timbrado en el repo común de CFDI.
///
/// <para>
/// <c>EnProceso</c> deja el comprobante en <c>TimbradoEnProceso</c> sin efecto
/// adicional; el <c>TimbradoPendienteWorker</c> (F12-PR2) reconcilia esos
/// pendientes por serie+folio contra el PAC.
/// </para>
///
/// <para>
/// Cada llamada al PAC deja una fila en <see cref="BitacoraIntentoTimbrado"/>
/// ([Decisión 01-G] G4), en el mismo change-tracker que la FSM — el
/// <c>SaveChanges</c> del handler persiste ambos o ninguno.
/// </para>
/// </summary>
public static class TimbradoEjecutor
{
    /// <summary>
    /// Timbra <paramref name="comprobante"/> con la solicitud
    /// <paramref name="emision"/> y aplica la transición de estado que
    /// corresponda. Devuelve el resultado crudo del PAC para que el handler
    /// decida efectos adicionales (eventos, write-back, rollback).
    /// </summary>
    public static async Task<TimbradoResultado> TimbrarYAplicarAsync(
        FacturacionDbContext db,
        Comprobante comprobante,
        CfdiEmision emision,
        ICfdiTimbradoPort fiscal,
        ICfdiRepositorioPort cfdiRepo,
        DateTimeOffset ahora,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? persistirEnProcesoAntesDelPac = null)
    {
        if (comprobante is Millet.Facturacion.Domain.Facturas.FacturaVenta venta
            && venta.ComportamientoFiscal == Millet.Facturacion.Domain.Facturas.ComportamientoFiscal.VentaActivoFijo)
        {
            var autorizacion = db.AutorizacionesVentaActivo.Local.FirstOrDefault(x => x.Id == venta.AutorizacionId)
                ?? await db.AutorizacionesVentaActivo.SingleOrDefaultAsync(x => x.Id == venta.AutorizacionId, cancellationToken)
                ?? throw new Millet.SharedKernel.Application.Exceptions.BusinessRuleException("AF_AUTORIZACION_REQUERIDA", "Se requiere autorización de venta.");
            autorizacion.ValidarParaTimbrar(decimal.Round((venta.Subtotal - venta.Descuento) * (venta.TipoCambio ?? 1), 2), ahora, venta.Id);
        }
        comprobante.MarcarTimbradoEnProceso();
        // 86e3t97tx: el reintento guarda aquí el estado «en proceso» ANTES del
        // PAC; si otro reintento ya lo tomó, el guardado falla y el PAC no se
        // llama (sin doble UUID). Las emisiones no lo usan (callback nulo).
        if (persistirEnProcesoAntesDelPac is not null)
            await persistirEnProcesoAntesDelPac(cancellationToken);
        var timbre = await fiscal.TimbrarAsync(emision, cancellationToken);

        switch (timbre.Estado)
        {
            case TimbradoEstado.Timbrado:
                Guid? cfdiArchivoId = null;
                if (!string.IsNullOrWhiteSpace(timbre.Uuid))
                {
                    cfdiArchivoId = await cfdiRepo.GuardarAsync(
                        new CfdiArchivoNuevo(
                            Uuid: timbre.Uuid!,
                            XmlContenido: timbre.XmlTimbrado ?? string.Empty,
                            PdfContenido: null,
                            SelloCfdi: timbre.SelloCfdi,
                            SelloSat: timbre.SelloSat,
                            NoCertificadoSat: timbre.NoCertificadoSat,
                            RfcProveedorCertificacion: timbre.RfcProveedorCertificacion,
                            FechaTimbrado: timbre.FechaTimbrado ?? ahora),
                        cancellationToken);
                }

                comprobante.MarcarTimbrado(
                    uuid: timbre.Uuid!,
                    selloCfdi: timbre.SelloCfdi,
                    selloSat: timbre.SelloSat,
                    noCertificadoSat: timbre.NoCertificadoSat,
                    fechaTimbrado: timbre.FechaTimbrado ?? ahora,
                    rfcProveedorCertificacion: timbre.RfcProveedorCertificacion,
                    cfdiArchivoId: cfdiArchivoId,
                    folioPac: timbre.FolioPac);
                // La metadata informativa no debe revertir un timbrado ya confirmado por el PAC.
                // Un archivo ajeno se conserva para diagnóstico, pero no se copian sus datos.
                var uuidXml = CadenaOriginalTimbre.UuidDesde(timbre.XmlTimbrado);
                if (uuidXml is null || string.Equals(uuidXml, timbre.Uuid, StringComparison.OrdinalIgnoreCase))
                    comprobante.RecalcularDatosTimbre(timbre.XmlTimbrado);
                if (comprobante.CadenaOriginalTimbre is null)
                    db.GetService<ILoggerFactory>().CreateLogger("Millet.Facturacion.Timbrado")
                        .LogWarning("El comprobante {ComprobanteId} quedó timbrado sin cadena original del timbre.", comprobante.Id);
                break;

            case TimbradoEstado.EnProceso:
                // Queda en TimbradoEnProceso; lo resuelve el worker (F12-PR2).
                break;

            case TimbradoEstado.Fallido:
                comprobante.MarcarTimbradoFallido(timbre.ErrorCodigo, timbre.ErrorMensaje);
                break;
        }

        // [Decisión 01-G] G4: bitácora del intento. Cuenta previos en BD +
        // los aún no persistidos en el change-tracker (misma emisión puede
        // timbrar factura + NCs de amortización antes del SaveChanges único).
        var previosLocales = db.ChangeTracker.Entries<BitacoraIntentoTimbrado>()
            .Count(e => e.State == EntityState.Added && e.Entity.ComprobanteId == comprobante.Id);
        var previos = previosLocales + await db.BitacorasIntentoTimbrado
            .CountAsync(b => b.ComprobanteId == comprobante.Id, cancellationToken);

        db.BitacorasIntentoTimbrado.Add(BitacoraIntentoTimbrado.Registrar(
            empresaId: comprobante.EmpresaId,
            comprobanteId: comprobante.Id,
            intentoNumero: previos + 1,
            resultado: timbre.Estado switch
            {
                TimbradoEstado.Timbrado => ResultadoIntentoTimbrado.Timbrado,
                TimbradoEstado.EnProceso => ResultadoIntentoTimbrado.EnProceso,
                _ => ResultadoIntentoTimbrado.Fallido,
            },
            errorCodigo: timbre.ErrorCodigo,
            errorMensaje: timbre.ErrorMensaje,
            registradoAt: ahora));

        return timbre;
    }
}
