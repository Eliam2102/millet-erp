using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Correo;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration.Tablero;
using Millet.SharedKernel.Domain.Integraciones;

namespace Millet.Compartido.Application.TableroIntegraciones;

public sealed class TableroIntegracionesOptions
{
    public const string SectionName = "Integraciones:Tablero";

    /// <summary>Apaga el worker de alertas (el tablero se sigue pudiendo consultar).</summary>
    public bool AlertasDeshabilitadas { get; set; }
    public int IntervaloSegundos { get; set; } = 60;
    /// <summary>Un mensaje pendiente o en reintento más viejo que esto marca el flujo como detenido.</summary>
    public int UmbralDetenidoMinutos { get; set; } = 15;
    /// <summary>
    /// Un mensaje que sigue reintentando y ya falló estas veces abre la alerta «Reintentos altos»
    /// (I13.6-a: «un mensaje que falla 5 veces»), aunque todavía no agote su máximo.
    /// </summary>
    public int UmbralReintentosAlerta { get; set; } = 5;
    public int MaxMensajes { get; set; } = 50;
    /// <summary>Correos que reciben las alertas y las recuperaciones. Vacío = solo queda en el tablero.</summary>
    public string[] Destinatarios { get; set; } = [];
}

public sealed record FuenteTableroResponse(
    string Clave,
    string Nombre,
    string Grupo,
    bool PermiteReintento,
    bool Disponible,
    string? ErrorLectura,
    int Pendientes,
    int Reintentando,
    int EnError,
    int OkUltimas24h,
    DateTimeOffset? UltimoExito,
    DateTimeOffset? PendienteMasAntiguo,
    bool Detenido,
    IReadOnlyList<MensajeIntegracion> Mensajes);

public sealed record AlertaIntegracionResponse(
    Guid Id,
    string FuenteClave,
    string FuenteNombre,
    TipoAlertaIntegracion Tipo,
    string Detalle,
    DateTimeOffset AbiertaEn,
    DateTimeOffset? NotificadaEn,
    DateTimeOffset? CerradaEn,
    DateTimeOffset? RecuperacionNotificadaEn);

public sealed record TableroIntegracionesResponse(
    DateTimeOffset GeneradoEn,
    int UmbralDetenidoMinutos,
    IReadOnlyList<FuenteTableroResponse> Fuentes,
    IReadOnlyList<AlertaIntegracionResponse> AlertasAbiertas);

/// <summary>Resultado de una evaluación de alertas (para el worker y las pruebas).</summary>
public sealed record EvaluacionAlertas(int Abiertas, int Cerradas, int CorreosEnviados);

/// <summary>
/// Tablero de integraciones (I13.6): junta las fuentes que registran los módulos
/// (<see cref="IFuenteTableroIntegracion"/>), reintenta a mano y evalúa las
/// alertas. Una fuente que no se puede leer no tumba el tablero: se muestra como
/// no disponible.
/// </summary>
public sealed class TableroIntegracionesServicio(
    IEnumerable<IFuenteTableroIntegracion> fuentes,
    CompartidoDbContext db,
    ICorreoSaliente correo,
    IClock clock,
    IOptionsMonitor<TableroIntegracionesOptions> opciones,
    ILogger<TableroIntegracionesServicio> logger)
{
    public async Task<TableroIntegracionesResponse> LeerAsync(CancellationToken cancellationToken)
    {
        var ahora = clock.UtcNow;
        var fotos = await FotosAsync(ahora, cancellationToken);
        var alertas = await db.AlertasIntegracion.AsNoTracking().Where(a => a.CerradaEn == null)
            .OrderByDescending(a => a.AbiertaEn).ToListAsync(cancellationToken);
        return new TableroIntegracionesResponse(ahora, opciones.CurrentValue.UmbralDetenidoMinutos, fotos,
            alertas.Select(Respuesta).ToList());
    }

    public async Task<IReadOnlyList<AlertaIntegracionResponse>> AlertasAsync(int limit, CancellationToken cancellationToken)
    {
        var lista = await db.AlertasIntegracion.AsNoTracking().OrderByDescending(a => a.AbiertaEn)
            .Take(Math.Clamp(limit, 1, 200)).ToListAsync(cancellationToken);
        return lista.Select(Respuesta).ToList();
    }

    public async Task ReintentarAsync(string clave, string mensajeId, CancellationToken cancellationToken)
    {
        var fuente = fuentes.FirstOrDefault(f => f.Clave == clave)
            ?? throw new EntityNotFoundException("INTEGRACION_NO_ENCONTRADA", "No existe esa integración en el tablero.");
        if (!fuente.PermiteReintento)
            throw new BusinessRuleException("INTEGRACION_SIN_REINTENTO",
                $"«{fuente.Nombre}» no se reintenta desde el tablero; se atiende en su propia pantalla.");
        if (!await fuente.ReintentarAsync(mensajeId, cancellationToken))
            throw new BusinessRuleException("INTEGRACION_MENSAJE_NO_REINTENTABLE",
                "Ese mensaje ya no se puede reintentar: ya se procesó o ya no existe. Actualiza el tablero.");
    }

    /// <summary>
    /// Abre una alerta por fuente y tipo cuando aparece la condición (mensajes en
    /// error, flujo detenido) y la cierra cuando desaparece. Avisa por correo al
    /// abrir y al recuperarse; si el correo falla, lo reintenta en la siguiente
    /// evaluación. La alerta se guarda antes de avisar: si no se puede guardar, no
    /// se manda correo. Una fuente que no se pudo leer no abre ni cierra nada.
    /// </summary>
    public async Task<EvaluacionAlertas> EvaluarAlertasAsync(CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var ahora = clock.UtcNow;
        var fotos = await FotosAsync(ahora, ct);
        var abiertas = await db.AlertasIntegracion.Where(a => a.CerradaEn == null).ToListAsync(ct);
        int nuevas = 0, cerradas = 0;

        foreach (var f in fotos.Where(f => f.Disponible))
        {
            var umbralReintentos = Math.Max(1, opciones.CurrentValue.UmbralReintentosAlerta);
            var conReintentosAltos = f.Mensajes.Count(m => m.Estado == EstadoMensajeIntegracion.Reintentando && m.Intentos >= umbralReintentos);
            var condiciones = new Dictionary<TipoAlertaIntegracion, string?>
            {
                [TipoAlertaIntegracion.MensajesEnError] = f.EnError > 0
                    ? $"{f.EnError} mensaje(s) agotaron sus reintentos y esperan reintento manual." : null,
                [TipoAlertaIntegracion.FlujoDetenido] = f.Detenido
                    ? $"Hay {f.Pendientes + f.Reintentando} mensaje(s) sin procesar; el más viejo es de {Hora(f.PendienteMasAntiguo!.Value)} " +
                      $"(más de {opciones.CurrentValue.UmbralDetenidoMinutos} min)." : null,
                [TipoAlertaIntegracion.ReintentosAltos] = conReintentosAltos > 0
                    ? $"{conReintentosAltos} mensaje(s) ya fallaron {umbralReintentos} veces o más y siguen reintentando." : null,
            };
            foreach (var (tipo, detalle) in condiciones)
            {
                var abierta = abiertas.FirstOrDefault(a => a.FuenteClave == f.Clave && a.Tipo == tipo);
                if (detalle is not null && abierta is null)
                {
                    var a = AlertaIntegracion.Abrir(f.Clave, f.Nombre, tipo, detalle, ahora);
                    db.AlertasIntegracion.Add(a);
                    abiertas.Add(a);
                    nuevas++;
                }
                else if (detalle is not null)
                {
                    abierta!.Actualizar(detalle);
                }
                else if (abierta is not null)
                {
                    abierta.Cerrar(ahora);
                    cerradas++;
                }
            }
        }

        // Primero se guardan las aperturas y cierres: si el guardado falla no sale ningún correo
        // (de lo contrario el aviso se repetiría en cada vuelta sin que la alerta exista).
        await db.SaveChangesAsync(ct);

        // Las cerradas que todavía no avisaron su recuperación (de esta corrida o de una con el correo caído).
        var porAvisarRecuperacion = await db.AlertasIntegracion
            .Where(a => a.CerradaEn != null && a.NotificadaEn != null && a.RecuperacionNotificadaEn == null).ToListAsync(ct);
        var cerradasAhora = abiertas.Where(a => !a.Abierta && a.NotificadaEn is not null);
        var correos = 0;
        foreach (var a in abiertas.Where(a => a.Abierta && a.NotificadaEn is null))
            if (await AvisarAsync(a, recuperacion: false, ct)) { a.MarcarNotificada(ahora); correos++; }
        foreach (var a in porAvisarRecuperacion.Concat(cerradasAhora).DistinctBy(a => a.Id))
            if (await AvisarAsync(a, recuperacion: true, ct)) { a.MarcarRecuperacionNotificada(ahora); correos++; }

        await db.SaveChangesAsync(ct);
        return new EvaluacionAlertas(nuevas, cerradas, correos);
    }

    private async Task<List<FuenteTableroResponse>> FotosAsync(DateTimeOffset ahora, CancellationToken ct)
    {
        var o = opciones.CurrentValue;
        var umbral = ahora.AddMinutes(-o.UmbralDetenidoMinutos);
        var lista = new List<FuenteTableroResponse>();
        foreach (var f in fuentes.OrderBy(f => f.Grupo).ThenBy(f => f.Nombre))
        {
            try
            {
                var r = await f.LeerAsync(ahora, o.MaxMensajes, ct);
                lista.Add(new(f.Clave, f.Nombre, f.Grupo, f.PermiteReintento, true, null, r.Pendientes, r.Reintentando,
                    r.EnError, r.OkUltimas24h, r.UltimoExito, r.PendienteMasAntiguo,
                    r.PendienteMasAntiguo is { } p && p < umbral, r.Mensajes));
            }
            catch (FuenteTableroSinConexionException ex)
            {
                lista.Add(new(f.Clave, f.Nombre, f.Grupo, false, false, ex.Message, 0, 0, 0, 0, null, null, false, []));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[I13.6] No se pudo leer la integración {Clave}.", f.Clave);
                lista.Add(new(f.Clave, f.Nombre, f.Grupo, false, false, "No se pudo leer el estado de esta integración.",
                    0, 0, 0, 0, null, null, false, []));
            }
        }
        return lista;
    }

    /// <returns><c>true</c> si el correo salió o no hay a quién avisar (la alerta queda en el tablero).</returns>
    private async Task<bool> AvisarAsync(AlertaIntegracion a, bool recuperacion, CancellationToken ct)
    {
        var para = opciones.CurrentValue.Destinatarios.Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
        if (para.Count == 0) return true;
        var asunto = recuperacion
            ? $"Integración recuperada: {a.FuenteNombre}"
            : $"Alerta de integración: {a.FuenteNombre} · {Titulo(a.Tipo)}";
        var cuerpo = recuperacion
            ? $"<p>La integración <b>{WebUtility.HtmlEncode(a.FuenteNombre)}</b> se recuperó a las {Hora(a.CerradaEn!.Value)}.</p>" +
              $"<p>La alerta «{Titulo(a.Tipo)}» estuvo abierta desde las {Hora(a.AbiertaEn)}.</p>"
            : $"<p>La integración <b>{WebUtility.HtmlEncode(a.FuenteNombre)}</b> requiere atención: {Titulo(a.Tipo).ToLowerInvariant()}.</p>" +
              $"<p>{WebUtility.HtmlEncode(a.Detalle)}</p><p>Revísala en Administración › Integraciones › Tablero.</p>";
        var r = await correo.EnviarAsync(new MensajeCorreo(null, "integraciones",
            recuperacion ? "alerta-integracion-recuperada" : "alerta-integracion", a.Id, para, asunto, cuerpo), ct);
        if (!r.FueAceptado && r.Estado != EstadoCorreoSaliente.Simulado)
        {
            logger.LogWarning("[I13.6] No salió el correo de la alerta {AlertaId}: {Codigo} {Motivo}.", a.Id, r.Codigo, r.Motivo);
            return false;
        }
        return true;
    }

    private static string Titulo(TipoAlertaIntegracion tipo) => tipo switch
    {
        TipoAlertaIntegracion.MensajesEnError => "Mensajes en error",
        TipoAlertaIntegracion.ReintentosAltos => "Reintentos altos",
        _ => "Flujo detenido",
    };

    /// <summary>Hora local del centro de Millet (UTC−6) para los textos de las alertas.</summary>
    private static string Hora(DateTimeOffset utc) =>
        utc.ToOffset(TimeSpan.FromHours(-6)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private static AlertaIntegracionResponse Respuesta(AlertaIntegracion a) =>
        new(a.Id, a.FuenteClave, a.FuenteNombre, a.Tipo, a.Detalle, a.AbiertaEn, a.NotificadaEn, a.CerradaEn, a.RecuperacionNotificadaEn);
}
