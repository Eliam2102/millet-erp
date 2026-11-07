using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application.Incidencias;

namespace Millet.CuentasPorPagar.Application.Incidencias;

/// <summary>
/// U-4 (86e3yzemw · ALM-costo-06): diferencia del neto de la factura contra la entrada.
/// <list type="bullet">
///   <item>Factura rechazada por tolerancia: documento = OC, detalle «tolerancia»; se resuelve cuando se captura
///   contra esa OC una factura que sí concilia (recaptura tras corregir la OC o con la factura correcta).</item>
///   <item>Factura enviada a revisión por diferencia de precio: documento = factura, detalle «revision»; se resuelve
///   al liberar la revisión.</item>
/// </list>
/// Responsable por omisión: quien capturó o envió a revisión (supuesto a validar; se reasigna desde «Mis incidencias»).
/// </summary>
public static class IncidenciasCxp
{
    public const string Modulo = "cuentas_por_pagar";
    public const string ClaveTolerancia = "tolerancia";
    public const string ClaveRevision = "revision";
    /// <summary>Motivos de revisión que son diferencia de dinero contra lo recibido (catálogo §5.3).</summary>
    public static readonly IReadOnlySet<string> MotivosRevisionDeCosto = new HashSet<string>(StringComparer.Ordinal) { "DIFERENCIA_PRECIO" };

    public static AbrirIncidenciaSolicitud RechazoPorTolerancia(Guid ocId, string ocFolio, Guid facturaId, string facturaEtiqueta,
        Guid? sucursalId, Guid responsable, string motivo) => new(
        Modulo, "orden_compra", ocId, $"OC {ocFolio} · factura {facturaEtiqueta}",
        PasosIncidencia.AlmCostoNetoFactura, Recortar(motivo), responsable, sucursalId, CodigoMotivo: "FACTURA_RECHAZADA_POR_TOLERANCIA",
        Detalle: "El neto de la factura no cuadra con la orden y lo recibido, y excede la tolerancia del proveedor. "
            + "Compras corrige la orden o el proveedor sustituye la factura; al capturar la factura que concilia, la incidencia se resuelve sola.",
        DetalleClave: ClaveTolerancia,
        PasoReanudacion: PasosIncidencia.CxpFacturaRevision,
        RutaReanudacion: $"/cxp/facturas/{facturaId}");

    public static AbrirIncidenciaSolicitud RevisionPorCosto(Guid facturaId, string facturaEtiqueta, Guid? sucursalId, Guid responsable,
        string motivoCodigo, string motivoNombre) => new(
        Modulo, "factura_proveedor", facturaId, $"Factura {facturaEtiqueta}",
        PasosIncidencia.AlmCostoNetoFactura, Recortar($"Factura enviada a revisión: {motivoNombre}."), responsable, sucursalId,
        CodigoMotivo: motivoCodigo,
        Detalle: "La factura no se paga mientras siga en revisión. Al liberar la revisión con la acción tomada, la incidencia se resuelve sola.",
        DetalleClave: ClaveRevision,
        PasoReanudacion: PasosIncidencia.CxpFacturaRevision,
        RutaReanudacion: $"/cxp/facturas/{facturaId}");

    public static string EtiquetaFactura(string? serie, string? folio, string? uuid) =>
        $"{serie}{folio}" is { Length: > 0 } sf ? sf : uuid ?? "(sin folio)";

    private static string Recortar(string texto) => texto.Length <= 1000 ? texto : texto[..1000];

    /// <summary>Después de guardar en CxP: un error aquí no deshace la operación; queda en el log.</summary>
    public static async Task SinRomperAsync(ILogger? logger, string que, Func<Task> accion)
    {
        try
        {
            await accion();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Incidencias U-4: no se pudo {Que}.", que);
        }
    }
}
