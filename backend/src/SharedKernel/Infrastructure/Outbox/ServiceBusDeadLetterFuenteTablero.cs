using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application.Integration.Tablero;

namespace Millet.SharedKernel.Infrastructure.Outbox;

/// <summary>
/// Cola de errores (dead-letter) de cada suscripción de Service Bus en el tablero
/// de integraciones (I13.6). Solo lectura: hace <i>peek</i> (no consume) de la
/// sub-cola <c>$DeadLetterQueue</c> de cada suscripción y muestra el tipo de
/// evento, el motivo y la descripción con que el listener lo descartó. El
/// reenvío al topic queda para otra entrega; por ahora se atiende revisando la
/// causa (p. ej. un evento que el listener no reconoce).
/// </summary>
/// <remarks>
/// El peek solo requiere el permiso Listen de la cadena de conexión (no Manage),
/// así que funciona con la misma conexión del outbox y con el emulador local.
/// </remarks>
public sealed class ServiceBusDeadLetterFuenteTablero(
    ServiceBusClient client, IReadOnlyList<SuscripcionServiceBus> suscripciones, ILogger<ServiceBusDeadLetterFuenteTablero> logger)
    : IFuenteTableroIntegracion
{
    /// <summary>Máximo de mensajes que se miran por suscripción en cada lectura.</summary>
    public const int MaximoPorSuscripcion = 100;

    public string Clave => "service-bus.dead-letter";
    public string Nombre => "Cola de errores de Service Bus (dead-letter)";
    public string Grupo => "Service Bus";
    public bool PermiteReintento => false;

    public async Task<ResumenFuenteIntegracion> LeerAsync(DateTimeOffset ahora, int maxMensajes, CancellationToken cancellationToken)
    {
        var todos = new List<MensajeIntegracion>();
        var total = 0;
        foreach (var s in suscripciones)
        {
            try
            {
                await using var receptor = client.CreateReceiver(s.Topic, s.Suscripcion,
                    new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter, ReceiveMode = ServiceBusReceiveMode.PeekLock });
                var mensajes = await receptor.PeekMessagesAsync(MaximoPorSuscripcion, cancellationToken: cancellationToken);
                total += mensajes.Count;
                todos.AddRange(mensajes.Select(m => new MensajeIntegracion(
                    $"{s.Topic}/{s.Suscripcion}/{m.SequenceNumber}",
                    $"{s.Topic} → {s.Suscripcion} · {(string.IsNullOrWhiteSpace(m.Subject) ? "sin tipo" : m.Subject)}",
                    EstadoMensajeIntegracion.Error,
                    m.DeliveryCount,
                    Motivo(m),
                    m.EnqueuedTime,
                    null)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Una suscripción que no existe en este namespace no tumba la lectura de las demás.
                logger.LogWarning(ex, "[I13.6] No se pudo leer la cola de errores de {Topic}/{Suscripcion}.", s.Topic, s.Suscripcion);
            }
        }

        var lista = todos.OrderByDescending(m => m.CreadoEn).Take(maxMensajes).ToList();
        return new ResumenFuenteIntegracion(0, 0, total, 0, null, null, lista);
    }

    public Task<bool> ReintentarAsync(string mensajeId, CancellationToken cancellationToken) => Task.FromResult(false);

    private static string Motivo(ServiceBusReceivedMessage m)
    {
        var motivo = string.IsNullOrWhiteSpace(m.DeadLetterReason) ? "Sin motivo" : m.DeadLetterReason;
        var texto = string.IsNullOrWhiteSpace(m.DeadLetterErrorDescription) ? motivo : $"{motivo}: {m.DeadLetterErrorDescription}";
        return texto.Length <= 500 ? texto : texto[..500];
    }
}

/// <summary>Suscripción de un topic de Service Bus cuya cola de errores muestra el tablero.</summary>
public sealed record SuscripcionServiceBus(string Topic, string Suscripcion)
{
    /// <summary>
    /// Suscripciones con consumidor del ERP: las de <c>infra/modules/servicebus.bicep</c> (incluye las del emulador
    /// <c>tools/servicebus-emulator/Config.json</c>); una prueba lo verifica. Sin las de «sin-consumidor-local».
    /// </summary>
    public static IReadOnlyList<SuscripcionServiceBus> DelErp { get; } =
    [
        new("integraciones-aw-events", "drop-subscription"),
        new("compras-events", "cuentas-por-pagar-subscription"),
        new("almacen-events", "cuentas-por-pagar-subscription"),
        new("almacen-events", "compras-subscription-almacen"),
        new("almacen-events", "activos-fijos-almacen"),
        new("almacen-events", "contabilidad-almacen-sub"),
        new("almacen-events", "comercio-exterior-almacen-sub"),
        new("cuentas-por-pagar-events", "compras-subscription"),
        new("cuentas-por-pagar-events", "almacen-subscription"),
        new("cuentas-por-pagar-events", "tesoreria-subscription"),
        new("cuentas-por-pagar-events", "activos-fijos-cxp"),
        new("cuentas-por-pagar-events", "contabilidad-cuentas-por-pagar-sub"),
        new("facturacion-events", "cuentas-por-cobrar-subscription"),
        new("facturacion-events", "integraciones-aw-subscription"),
        new("facturacion-events", "tesoreria-subscription"),
        new("facturacion-events", "activos-fijos-ventas"),
        new("facturacion-events", "contabilidad-facturacion-sub"),
        new("facturacion-events", "comercio-exterior-facturacion-sub"),
        new("cuentas-por-cobrar-events", "tesoreria-subscription"),
        new("tesoreria-events", "cuentas-por-pagar-tesoreria-sub"),
        new("tesoreria-events", "facturacion-tesoreria-sub"),
        new("tesoreria-events", "cuentas-por-cobrar-tesoreria-sub"),
        new("tesoreria-events", "contabilidad-tesoreria-sub"),
        new("tesoreria-events", "comercio-exterior-tesoreria-sub"),
        new("comercio-exterior-events", "tesoreria-comercio-exterior-sub"),
        new("comercio-exterior-events", "cuentas-por-pagar-ce-sub"),
        // Solo en Azure (el emulador local aún no las declara): en local su lectura falla y se omite.
        new("comercio-exterior-events", "almacen-comercio-exterior-sub"),
        new("comercio-exterior-events", "facturacion-ce-sub"),
    ];
}
