using System.Globalization;
using Millet.CuentasPorCobrar.Domain.Conciliacion;
using Millet.SharedKernel.Application.Incidencias;

namespace Millet.CuentasPorCobrar.Application.Conciliacion;

/// <summary>
/// M-236: cómo abre y cierra CxC las incidencias de las diferencias de la conciliación diaria. Documento = comprobante de
/// Facturación que no cuadra; clave = día conciliado + tipo de diferencia (el mismo comprobante puede diferir en días
/// distintos). La apertura y el cierre usan exactamente estas claves.
/// </summary>
public static class IncidenciasConciliacion
{
    public const string Modulo = "cuentas_por_cobrar";
    public const string TipoDocumento = "comprobante_facturacion";
    public const string RutaRevision = "/cxc/conciliacion";

    /// <summary>Corridas previas que se revisan para cerrar solas las incidencias ya reflejadas.</summary>
    public const int VentanaSeguimientoDias = 120;

    /// <summary>Tope de días que una corrida vuelve a comparar contra Facturación para cerrar incidencias (costo acotado).</summary>
    public const int MaxDiasRevisionPorCorrida = 15;

    public static string Clave(DateOnly fecha, TipoDiferenciaConciliacion tipo) =>
        string.Create(CultureInfo.InvariantCulture, $"{fecha:yyyy-MM-dd}|{tipo}");

    public static AbrirIncidenciaSolicitud Solicitud(DateOnly fecha, DiferenciaCalculada d, Guid responsable) => new(
        Modulo, TipoDocumento, d.ComprobanteId, $"{d.TipoComprobante} {d.Folio}",
        PasosIncidencia.CxcConciliacionDiferencia, d.CausaProbable, responsable,
        CodigoMotivo: $"CXC_CONCILIACION_{d.Tipo.ToString().ToUpperInvariant()}",
        Detalle: Detalle(fecha, d),
        DetalleClave: Clave(fecha, d.Tipo),
        PasoReanudacion: PasosIncidencia.CxcConciliacionRevision,
        RutaReanudacion: RutaRevision);

    private static string Detalle(DateOnly fecha, DiferenciaCalculada d)
    {
        var cartera = d.ImporteCartera is decimal c ? c.ToString("N2", CultureInfo.InvariantCulture) : "sin registro";
        return $"Día conciliado {fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}. " +
            $"Importe en Facturación {d.ImporteFacturacion.ToString("N2", CultureInfo.InvariantCulture)}; en cartera {cartera}. " +
            "Se cierra sola cuando la siguiente conciliación ya no encuentra la diferencia.";
    }

    public static string TextoCierreAutomatico(DateOnly fecha) =>
        string.Create(CultureInfo.InvariantCulture, $"La conciliación del {fecha:dd/MM/yyyy} ya no encuentra la diferencia: se refleja en cartera.");
}
