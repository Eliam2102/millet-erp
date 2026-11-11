using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Millet.Almacen.Application.AppAlmacenes;

/// <summary>Acuse al mensaje de la app (RECIBIDO, DUPLICADO o ERROR) con el folio del ERP y, si falla, el texto del error.</summary>
public sealed record AcuseApp(string? MensajeId, string Estado, string? Folio, string? Error);

/// <summary>
/// Renglón de <c>alm_mensaje</c> que la app dejó para el ERP (v8). <see cref="Secuencia"/> es el <c>id</c> del renglón (llave del
/// acuse); un reenvío es otro renglón con el mismo <see cref="MensajeId"/> e <see cref="Intento"/>+1; <see cref="Sobre"/> es el
/// sobre JSON completo (columna <c>contenido</c>). Ver docs/integration/11.
/// </summary>
public sealed record AlmMensajeEntrante(
    long Secuencia, string MensajeId, int Intento, string Tipo, string? Referencia, DateTimeOffset Fecha, string Sobre);

/// <summary>
/// Puerto de la tabla <c>alm_mensaje</c> (la app escribe el mensaje; el ERP escribe solo el acuse en el mismo renglón
/// y no borra). Adaptadores en Integraciones.Aw: simulado (por omisión) y SQL (apagado hasta que exista la tabla).
/// </summary>
public interface IAlmMensajeOrigen
{
    /// <summary>
    /// Renglones sin acuse que son el último intento de su mensaje, en orden del primer renglón de cada mensaje, hasta
    /// <paramref name="max"/>. Excluye a los de una referencia frenada por un mensaje anterior cuyo último intento sigue en ERROR.
    /// </summary>
    Task<IReadOnlyList<AlmMensajeEntrante>> LeerPendientesAsync(int max, CancellationToken ct);

    /// <summary>Escribe el acuse en el renglón; repetirlo sobre un renglón ya acusado no cambia nada.</summary>
    Task EscribirAcuseAsync(long secuencia, AcuseApp acuse, CancellationToken ct);

    /// <summary>
    /// M-283: mensajes (último intento de cada uno) que la app escribió hace más de <paramref name="segundos"/> y siguen sin acuse.
    /// La antigüedad se mide con el reloj de la base de la app (sus fechas son hora local, no UTC).
    /// </summary>
    Task<AlmMensajeSinAcuse> ContarSinAcuseAntiguosAsync(int segundos, CancellationToken ct);
}

/// <summary>Cuántos mensajes llevan más del plazo sin acuse y cuánto lleva el más viejo (0 si no hay).</summary>
public readonly record struct AlmMensajeSinAcuse(int Cantidad, int MasAntiguoSegundos);

/// <summary>Aplica un sobre ya parseado (mismo manejador que el endpoint HTTP) y devuelve su acuse. Implementado en Api.</summary>
public interface IProcesadorMensajeApp
{
    Task<AcuseApp> ProcesarAsync(JsonElement root, CancellationToken ct);
}

/// <summary>
/// Sección <c>Integraciones:AlmMensaje</c>. Apagado por omisión: sin <see cref="Habilitado"/> el ERP no lee la tabla.
/// <see cref="Origen"/>: <c>Simulado</c> (por omisión) o <c>Sql</c> (cadena <c>ConnectionStrings:AlmacenesAppDb</c>).
/// </summary>
public sealed class AlmMensajeOptions
{
    public const string SectionName = "Integraciones:AlmMensaje";

    public bool Habilitado { get; init; }
    public string Origen { get; init; } = "Simulado";
    public int IntervaloSegundos { get; init; } = 10;
    public int Lote { get; init; } = 100;
    /// <summary>Fallas seguidas del ERP con un mismo renglón antes de acusarlo ERROR (el destino de lo que falla siempre).</summary>
    public int MaxIntentos { get; init; } = 5;
    /// <summary>
    /// H1 (86e3z5w25): una salida cuya cuenta exige área o equipo (regla C-03) y no los trae se acusa ERROR en vez de aceptarse.
    /// Apagado mientras la app de TI no mande <c>area</c>/<c>equipo</c>: sin ellos la salida se acepta y su póliza queda en Rechazos
    /// con «Dato faltante», como hoy. Un área o equipo inválido siempre es ERROR.
    /// </summary>
    public bool ExigirAreaEquipo { get; init; }

    /// <summary>
    /// M-283 (contrato v9 §4): lee <c>alm_carga_inicial</c> (la carga de número más alto) y acusa APLICADO / ERROR por renglón.
    /// Apagado por omisión; se enciende cuando TI avise el número de carga. Usa el mismo <see cref="Origen"/> y cadena.
    /// </summary>
    public bool CargaInicialHabilitada { get; init; }

    /// <summary>M-283 (acuerdo 5-nov con TI): minutos sin acuse para avisar al jefe de Almacén. 0 apaga la alarma.</summary>
    public int AlarmaSinAcuseMinutos { get; init; } = 5;

    /// <summary>Cada cuántos minutos se repite el aviso mientras siga habiendo mensajes sin acuse (no cada ciclo de 10 s).</summary>
    public int AlarmaReavisoMinutos { get; init; } = 60;
}

public readonly record struct AlmMensajeTick(int Procesados, int Errores, int Frenados, int Fallas);

/// <summary>
/// Lee <c>alm_mensaje</c> y escribe el acuse de cada renglón (acuerdo 5-nov con TI). Orden estricto por secuencia; un ERROR
/// frena el resto de su referencia (los siguientes esperan); una falla del ERP no escribe acuse (se reintenta en el
/// siguiente ciclo, sin duplicar porque el manejador es idempotente por <c>mensajeId</c>) y tras
/// <see cref="AlmMensajeOptions.MaxIntentos"/> se acusa ERROR para no atorar la fila. Nunca registra el sobre.
/// </summary>
public sealed class AlmMensajeLector(
    IAlmMensajeOrigen origen, IServiceScopeFactory scopes, IOptions<AlmMensajeOptions> options, ILogger<AlmMensajeLector> logger)
{
    private readonly ConcurrentDictionary<long, int> _intentos = new();

    public async Task<AlmMensajeTick> ProcesarAsync(CancellationToken ct)
    {
        var pendientes = await origen.LeerPendientesAsync(Math.Max(1, options.Value.Lote), ct);
        var frenadas = new HashSet<string>(StringComparer.Ordinal);
        int procesados = 0, errores = 0, frenados = 0, fallas = 0;
        foreach (var m in pendientes)
        {
            if (m.Referencia is not null && frenadas.Contains(m.Referencia)) { frenados++; continue; }
            AcuseApp acuse;
            try { acuse = await AplicarAsync(m, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                fallas++;
                var intento = _intentos.AddOrUpdate(m.Secuencia, 1, (_, n) => n + 1);
                logger.LogError(ex, "[AlmMensajeLector] Falla al aplicar {Tipo} {MensajeId} (intento {Intento}).", m.Tipo, m.MensajeId, intento);
                if (m.Referencia is not null) frenadas.Add(m.Referencia);
                if (intento < Math.Max(1, options.Value.MaxIntentos)) continue;
                acuse = new AcuseApp(m.MensajeId, "ERROR", null, "Error interno del ERP al procesar el mensaje; revisar en el ERP.");
            }
            _intentos.TryRemove(m.Secuencia, out _);
            await origen.EscribirAcuseAsync(m.Secuencia, acuse, ct);
            procesados++;
            if (acuse.Estado == "ERROR")
            {
                errores++;
                if (m.Referencia is not null) frenadas.Add(m.Referencia);
            }
        }
        return new AlmMensajeTick(procesados, errores, frenados, fallas);
    }

    private async Task<AcuseApp> AplicarAsync(AlmMensajeEntrante m, CancellationToken ct)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(m.Sobre); }
        catch (JsonException) { return new AcuseApp(m.MensajeId, "ERROR", null, "sobre no es JSON válido"); }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("mensajeId", out var id)
                || id.ValueKind != JsonValueKind.String || id.GetString() != m.MensajeId)
                return new AcuseApp(m.MensajeId, "ERROR", null, "mensaje_id no coincide con el mensajeId del sobre");
            // Un ámbito por mensaje: si falla, su DbContext sucio no contamina al siguiente.
            using var scope = scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IProcesadorMensajeApp>().ProcesarAsync(doc.RootElement, ct);
        }
    }
}
