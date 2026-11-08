namespace Millet.SharedKernel.Application.Integration.Tablero;

/// <summary>Estado de un mensaje de integración en el tablero (I13.6).</summary>
public enum EstadoMensajeIntegracion : short
{
    /// <summary>Esperando su primer intento.</summary>
    Pendiente = 1,
    /// <summary>Falló al menos una vez y el sistema lo vuelve a intentar solo.</summary>
    Reintentando = 2,
    /// <summary>Agotó sus reintentos: ya no se intenta solo; se reintenta a mano.</summary>
    Error = 3,
    /// <summary>Entregado o procesado.</summary>
    Ok = 4,
}

/// <summary>Un mensaje (aviso, evento, envío) de una integración tal como lo muestra el tablero.</summary>
/// <param name="Id">Identificador dentro de la fuente; es el que recibe <see cref="IFuenteTableroIntegracion.ReintentarAsync"/>.</param>
/// <param name="Referencia">Lo que el usuario reconoce: tipo de evento, folio, documento.</param>
public sealed record MensajeIntegracion(
    string Id,
    string Referencia,
    EstadoMensajeIntegracion Estado,
    int Intentos,
    string? UltimoError,
    DateTimeOffset CreadoEn,
    DateTimeOffset? ProcesadoEn);

/// <summary>Foto de una integración: conteos y los mensajes más relevantes (primero los que piden atención).</summary>
/// <param name="OkUltimas24h">Procesados con éxito en las últimas 24 horas.</param>
/// <param name="PendienteMasAntiguo">Creación del pendiente o en reintento más viejo; sirve para detectar un flujo detenido.</param>
public sealed record ResumenFuenteIntegracion(
    int Pendientes,
    int Reintentando,
    int EnError,
    int OkUltimas24h,
    DateTimeOffset? UltimoExito,
    DateTimeOffset? PendienteMasAntiguo,
    IReadOnlyList<MensajeIntegracion> Mensajes)
{
    public static ResumenFuenteIntegracion Vacio { get; } = new(0, 0, 0, 0, null, null, []);
}

/// <summary>
/// Una integración que el tablero de integraciones (I13.6) puede mostrar y, si
/// lo permite, reintentar a mano. Cada módulo registra las suyas en DI; el
/// tablero las junta. La fuente lee su propio almacén (outbox, cola, bitácora)
/// y nunca escribe en tablas de otro módulo.
/// </summary>
public interface IFuenteTableroIntegracion
{
    /// <summary>Clave estable (minúsculas, con puntos): <c>outbox.compras</c>, <c>compras.envios-almacenes</c>.</summary>
    string Clave { get; }

    /// <summary>Nombre para el usuario.</summary>
    string Nombre { get; }

    /// <summary>Agrupación del tablero: «Service Bus», «A+W», «Correo», «SAT»…</summary>
    string Grupo { get; }

    bool PermiteReintento { get; }

    /// <param name="maxMensajes">Cuántos mensajes devolver como máximo (los que piden atención primero).</param>
    Task<ResumenFuenteIntegracion> LeerAsync(DateTimeOffset ahora, int maxMensajes, CancellationToken cancellationToken);

    /// <summary>Vuelve a poner el mensaje en la cola de su worker. <c>false</c> si no existe o ya no se puede reintentar.</summary>
    Task<bool> ReintentarAsync(string mensajeId, CancellationToken cancellationToken);
}

/// <summary>
/// La fuente no tiene conexión configurada en este ambiente (p. ej. Service Bus sin cadena de
/// conexión en local). El tablero la muestra «Sin lectura» con este mensaje, sin abrir alertas.
/// </summary>
public sealed class FuenteTableroSinConexionException(string mensaje) : Exception(mensaje);
