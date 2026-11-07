using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application.Incidencias;

namespace Millet.Almacen.Application.Incidencias;

/// <summary>
/// U-4 (86e3yzemw): cómo abre y resuelve Almacén las incidencias de sus pasos de la oleada 1. Documento, paso, ruta
/// para retomar y clave de detalle salen de aquí para que la apertura (rechazo) y la resolución (paso retomado)
/// usen exactamente las mismas claves.
/// <list type="bullet">
///   <item>Artículo-ubicación: documento = artículo, detalle = ubicación; se resuelve al asignarlo a esa ubicación.</item>
///   <item>Diferencia de recepción: documento = OC, detalle = línea de OC; se resuelve al recibir esa línea.</item>
///   <item>Vale: documento = vale; se resuelve al regularizarlo con su requisición.</item>
///   <item>Acuse de surtido: documento = salida; se resuelve con la devolución interna de esa salida.</item>
/// </list>
/// Responsable por omisión: quien recibió el rechazo (o quien registró el vale). Es un supuesto a validar con Millet;
/// el responsable la reasigna desde «Mis incidencias».
/// </summary>
public static class IncidenciasAlmacen
{
    public const string Modulo = "almacen";

    public static AbrirIncidenciaSolicitud ArticuloSinUbicacion(Guid articuloId, Guid ubicacionId, string ubicacionClave,
        Guid? sucursalId, string motivo) => new(
        Modulo, "articulo", articuloId, $"Artículo en ubicación {ubicacionClave}",
        PasosIncidencia.AlmUbicacionesAsignacion, motivo, Guid.Empty, sucursalId,
        Detalle: "Asigna el artículo a la ubicación y vuelve a registrar la entrada.",
        DetalleClave: ubicacionId.ToString(),
        PasoReanudacion: PasosIncidencia.AlmUbicacionesAsignar,
        RutaReanudacion: $"/almacen/asignaciones?articuloId={articuloId}");

    public static string ClaveRechazo(Guid lineaOcId) => $"rechazo:{lineaOcId}";
    public static string ClaveExcede(Guid lineaOcId) => $"excede:{lineaOcId}";

    public static AbrirIncidenciaSolicitud RechazoRecepcion(Guid ocId, string ocFolio, Guid recepcionId, string? recepcionFolio,
        Guid lineaOcId, Guid? sucursalId, Guid responsable, string motivo, string detalle) => new(
        Modulo, "orden_compra", ocId, Etiqueta(ocFolio, recepcionFolio),
        PasosIncidencia.AlmRecepcionDiferenciaOc, motivo, responsable, sucursalId, CodigoMotivo: "RECEPCION_RECHAZO",
        Detalle: detalle, DetalleClave: ClaveRechazo(lineaOcId),
        PasoReanudacion: PasosIncidencia.AlmRecepcionRegistrar,
        RutaReanudacion: $"/almacen/recepciones/{recepcionId}");

    public static AbrirIncidenciaSolicitud ExcedePendiente(Guid ocId, string ocFolio, Guid lineaOcId, Guid? sucursalId, string motivo) => new(
        Modulo, "orden_compra", ocId, Etiqueta(ocFolio, null),
        PasosIncidencia.AlmRecepcionDiferenciaOc, motivo, Guid.Empty, sucursalId,
        Detalle: "Lo recibido supera lo pendiente de la orden. Recibe solo lo pendiente o pide a Compras que corrija la orden.",
        DetalleClave: ClaveExcede(lineaOcId),
        PasoReanudacion: PasosIncidencia.AlmRecepcionRegistrar,
        RutaReanudacion: $"/compras/ordenes/{ocId}");

    public static AbrirIncidenciaSolicitud ValeSinRegularizar(Guid valeId, string? folio, Guid? sucursalId, Guid responsable,
        string motivo, string? codigo, string detalle) => new(
        Modulo, "vale", valeId, $"Vale {folio ?? "(sin folio)"}",
        PasosIncidencia.AlmValeRectificar, motivo, responsable, sucursalId, CodigoMotivo: codigo, Detalle: detalle,
        PasoReanudacion: PasosIncidencia.AlmValeRegularizar,
        RutaReanudacion: $"/almacen/salidas/{valeId}");

    private static string Etiqueta(string ocFolio, string? recepcionFolio) =>
        recepcionFolio is null ? $"OC {ocFolio}" : $"OC {ocFolio} · {recepcionFolio}";

    /// <summary>
    /// Abre o resuelve después de que el módulo ya guardó: un error aquí no debe deshacer ni ocultar la operación
    /// que sí se registró; queda en el log y la incidencia se puede abrir a mano.
    /// </summary>
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
