using System.Text.Json;

// ============================================================================
// MilletAwPedidoNotify — nudge de la ingesta de pedidos A+W (ADR-0048 PR7).
//
// La customización de A+W lo llama DESPUÉS de insertar la solicitud en
// MILLET_INTEGRACION.dbo.aw_solicitud_pedido:
//
//     MilletAwPedidoNotify.exe [numero_pedido] [operacion]
//
// Los argumentos son SOLO para el log local — el exe no manda datos: hace un
// POST vacío al endpoint /api/v1/integraciones/aw/pedidos/nudge del ERP con
// la API key del appsettings.json, y sale. BEST-EFFORT por diseño: sin
// reintentos — si falla (internet caído, ERP reiniciando), el polling normal
// del ERP drena la cola igual; la tabla-puente es la fuente de verdad.
//
// Exit codes: 0 = nudge entregado (2xx) · 1 = no entregado (no es error de
// negocio; A+W puede ignorarlo).
// ============================================================================

var baseDir = AppContext.BaseDirectory;
var logPath = Path.Combine(baseDir, "aw-pedido-notify.log");
var pedido = args.Length > 0 ? args[0] : "-";
var operacion = args.Length > 1 ? args[1] : "-";

try
{
    var configPath = Path.Combine(baseDir, "appsettings.json");
    var config = JsonSerializer.Deserialize<Config>(
        File.ReadAllText(configPath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("appsettings.json vacío o inválido.");

    if (string.IsNullOrWhiteSpace(config.NudgeUrl) || string.IsNullOrWhiteSpace(config.ApiKey))
        throw new InvalidOperationException("Configurar NudgeUrl y ApiKey en appsettings.json.");

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds) };
    using var request = new HttpRequestMessage(HttpMethod.Post, config.NudgeUrl);
    request.Headers.Add("X-Millet-Nudge-Key", config.ApiKey);

    using var response = http.Send(request);
    var ok = response.IsSuccessStatusCode;

    Log(logPath, $"pedido={pedido} operacion={operacion} status={(int)response.StatusCode} ok={ok}");
    return ok ? 0 : 1;
}
catch (Exception ex)
{
    // Nunca reventar hacia A+W: loggear y salir con 1.
    Log(logPath, $"pedido={pedido} operacion={operacion} ERROR={ex.GetType().Name}: {ex.Message}");
    return 1;
}

static void Log(string path, string mensaje)
{
    try
    {
        File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {mensaje}{Environment.NewLine}");
    }
    catch
    {
        // El log jamás debe tumbar el nudge.
    }
}

internal sealed record Config(
    string NudgeUrl,
    string ApiKey,
    int TimeoutSeconds = 5);
