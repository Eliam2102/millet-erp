using Microsoft.EntityFrameworkCore;
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
        CancellationToken cancellationToken)
    {
        comprobante.MarcarTimbradoEnProceso();
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
