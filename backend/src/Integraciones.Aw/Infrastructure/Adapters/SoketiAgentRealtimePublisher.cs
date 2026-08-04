using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Domain;
using PusherServer;

namespace Millet.Integraciones.Aw.Infrastructure.Adapters;

/// <summary>
/// Implementación productiva de <see cref="IAgentRealtimePublisher"/>
/// usando el SDK <c>PusherServer</c> contra el Soketi hosteado por Glass
/// Agent. Soketi es un broker open-source compatible con el protocolo
/// Pusher.
///
/// <para>
/// <b>Contract con Agent</b> (spec recibida 2026-05-18):
/// <list type="bullet">
///   <item>Canal: <c>private-user-{operator_user_id}</c> — el
///         <c>operator_user_id</c> se parsea de <see cref="EntidadExterna.PayloadOriginal"/>
///         (campo JSON enviado por Glass Agent en el POST original).</item>
///   <item>Evento: <c>aw-cotizacion-actualizada</c> (único; UI discrimina por
///         <c>estado</c> int del payload).</item>
///   <item>Payload en <b>snake_case</b> (distinto del API REST que usa
///         camelCase) — contrato directo con Agent.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Best-effort por diseño</b>: cualquier excepción (Soketi down,
/// network, secret inválido, parsing de operator_user_id) se loguea como
/// warning y se traga. El outcome del flow del worker ya está persistido
/// en BD; el push es notificación secundaria.
/// </para>
/// </summary>
public sealed class SoketiAgentRealtimePublisher : IAgentRealtimePublisher
{
    private readonly Pusher _pusher;
    private readonly SoketiOptions _options;
    private readonly ILogger<SoketiAgentRealtimePublisher> _logger;

    public SoketiAgentRealtimePublisher(
        IOptions<SoketiOptions> options,
        ILogger<SoketiAgentRealtimePublisher> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (!_options.IsEnabled)
        {
            throw new InvalidOperationException(
                "SoketiAgentRealtimePublisher instanciado sin config válida. " +
                "El DI debió seleccionar NoOpAgentRealtimePublisher.");
        }

        // PusherOptions con HostName/Port directos (Soketi self-hosted),
        // no cluster cloud. Encrypted=true para wss://.
        _pusher = new Pusher(
            _options.AppId,
            _options.Key,
            _options.Secret,
            new PusherOptions
            {
                HostName = _options.Host,
                Port = _options.Port,
                Encrypted = _options.UseTls,
                Cluster = string.Empty,
            });
    }

    public async Task PublishCotizacionActualizadaAsync(
        EntidadExterna entidad,
        CancellationToken cancellationToken)
    {
        var operatorUserId = TryExtractOperatorUserId(entidad);
        if (operatorUserId is null)
        {
            _logger.LogWarning(
                "Soketi push skipped: operator_user_id ausente en PayloadOriginal. " +
                "aggregate={Id} quoteRef={QuoteRef} (Glass Agent < v2.5.0?)",
                entidad.Id, entidad.ReferenciaExterna);
            return;
        }

        var channel = $"{_options.ChannelPrefix}{operatorUserId.Value}";
        // pdf_url apunta al endpoint proxy autenticado del ERP (no a la URL
        // interna del blob). El Agent lo consume con su Bearer JWT. Solo
        // presente cuando el PDF ya está adjunto.
        var pdfUrl = entidad.PdfBlobUrl is null
            ? null
            : $"/api/v1/integraciones/aw/cotizaciones/{entidad.Id}/pdf";
        var data = new
        {
            erp_id = entidad.Id,
            quote_reference = entidad.ReferenciaExterna,
            sucursal = entidad.Sucursal,
            estado = (int)entidad.Estado,
            aw_doc_id = entidad.AwDocId,
            // correlated_at autoritativo (estable, no cambia al adjuntar el PDF).
            // Evita que el evento del PDF sobreescriba aw_correlated_at con la hora
            // del PDF en el Agent (el frontend ya NO debe caer a updated_at).
            correlated_at = entidad.CorrelatedAt?.ToString("o"),
            last_error = entidad.LastError,
            pdf_url = pdfUrl,
            pdf_filename = entidad.PdfFilename,
            // Hora REAL de exportación del PDF por A+W (no la de subida/correlación).
            pdf_uploaded_at = entidad.PdfUploadedAt?.ToString("o"),
            updated_at = entidad.UpdatedAt.ToString("o"),
        };

        try
        {
            var result = await _pusher.TriggerAsync(channel, _options.EventName, data);
            _logger.LogDebug(
                "Soketi push OK. channel={Channel} event={Event} aggregate={Id} estado={Estado} status={Status}",
                channel, _options.EventName, entidad.Id, entidad.Estado, result.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort: el outcome ya está en BD. No fallar el flow
            // del worker por un problema de push.
            _logger.LogWarning(ex,
                "Soketi push failed. channel={Channel} aggregate={Id} estado={Estado} — Agent puede recuperar via polling al GET /api/v1/integraciones/aw/cotizaciones/{IdPath}.",
                channel, entidad.Id, entidad.Estado, entidad.Id);
        }
    }

    /// <summary>
    /// Parsea <c>operator_user_id</c> del JSON en
    /// <see cref="EntidadExterna.PayloadOriginal"/>. Retorna null si:
    /// el JSON no parsea, no tiene la propiedad, el valor no es int, o
    /// el int es ≤ 0 (defensive: 0 sería un canal inválido).
    /// </summary>
    /// <remarks>
    /// Glass Agent v2.5.0+ siempre incluye este campo per spec del
    /// 2026-05-18. Para versiones anteriores el push se skipea
    /// silenciosamente (con warning).
    /// </remarks>
    internal static int? TryExtractOperatorUserId(EntidadExterna entidad)
    {
        if (string.IsNullOrWhiteSpace(entidad.PayloadOriginal))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(entidad.PayloadOriginal);
            if (!doc.RootElement.TryGetProperty("operator_user_id", out var prop))
            {
                return null;
            }
            // El campo viene como int del lado de Agent (operator_user_id
            // de su BD MySQL). Defensive: aceptar también string si
            // alguna versión lo serializa así.
            int? value = prop.ValueKind switch
            {
                JsonValueKind.Number when prop.TryGetInt32(out var n) => n,
                JsonValueKind.String when int.TryParse(prop.GetString(), out var s) => s,
                _ => null,
            };
            return value is > 0 ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
