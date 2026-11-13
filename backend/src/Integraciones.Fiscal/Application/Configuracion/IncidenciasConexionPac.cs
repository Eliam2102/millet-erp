using Microsoft.Extensions.Logging;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Incidencias;

namespace Millet.Integraciones.Fiscal.Application.Configuracion;

/// <summary>
/// M-232: incidencia común cuando el timbrador rechaza la conexión (al probar o al guardar activa) y su cierre
/// cuando una prueba vuelve a salir bien. Es de mejor esfuerzo: el puerto guarda en su propia transacción y
/// un fallo al abrir o resolver solo se registra en el log, sin tapar el resultado de la prueba.
/// </summary>
internal static class IncidenciasConexionPac
{
    private const string Modulo = "integraciones_fiscal";
    private const string TipoDocumento = "configuracion_pac";
    private const string Ruta = "/admin/integraciones/fiscal";

    /// <summary>
    /// Fallas que no son del timbrador: sin credenciales capturadas (422) o integración apagada en el
    /// ambiente (501). No abren incidencia.
    /// </summary>
    public static bool EsFallaDelTimbrador(PingResultDto r) => !r.Exitosa && r.StatusCode is not (422 or 501);

    public static async Task AbrirAsync(IIncidenciasPort? incidencias, ILogger? logger, Guid documentoId,
        Guid? usuarioId, string baseUrl, PingResultDto resultado, string mensajeNormalizado, CancellationToken ct)
    {
        if (incidencias is null || usuarioId is not Guid responsable || !EsFallaDelTimbrador(resultado))
            return;
        try
        {
            await incidencias.AbrirAsync(new AbrirIncidenciaSolicitud(
                Modulo, TipoDocumento, documentoId, $"Timbrador · {baseUrl}",
                PasosIncidencia.FisPacConexionRechazada, mensajeNormalizado, responsable,
                CodigoMotivo: resultado.StatusCode == 0 ? "PAC_SIN_RESPUESTA" : $"PAC_HTTP_{resultado.StatusCode}",
                // Mensaje textual del timbrador: solo en la incidencia (bandeja interna), nunca en la respuesta.
                Detalle: $"HTTP {resultado.StatusCode} · {resultado.Mensaje}",
                PasoReanudacion: PasosIncidencia.FisPacConfiguracion,
                RutaReanudacion: Ruta), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "[M-232] No se pudo abrir la incidencia de conexión rechazada del PAC {DocumentoId}.", documentoId);
        }
    }

    public static async Task ResolverAsync(IIncidenciasPort? incidencias, ILogger? logger, Guid documentoId, CancellationToken ct)
    {
        if (incidencias is null) return;
        try
        {
            await incidencias.ResolverPorDocumentoAsync(PasosIncidencia.FisPacConexionRechazada, documentoId, null,
                "La prueba de conexión con el timbrador volvió a ser exitosa.", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "[M-232] No se pudo cerrar la incidencia de conexión del PAC {DocumentoId}.", documentoId);
        }
    }
}
