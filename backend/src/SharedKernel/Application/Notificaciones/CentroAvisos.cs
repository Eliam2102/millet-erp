using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.Application.Notificaciones;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SincronizarAvisosSolicitud(Guid? SucursalId = null, string? CursorDeteccion = null,
    string? Continuacion = null, int PageSize = 20, string? Modulo = null, string Condicion = "Todas");
public sealed record FuenteAvisosEstado(string Id, string Estado, string? Mensaje = null);
public sealed record AvisoCentroItem(AvisoUsuario Aviso, bool Leido);
public sealed record NovedadesFuente(string Id, int Cantidad);
public sealed record NovedadesAvisos(bool HayNovedades, int CantidadConocida, string HastaSecSeguro, IReadOnlyList<NovedadesFuente> PorFuente);
public sealed record CentroAvisosRespuesta(ContextoAvisos Contexto, DateTimeOffset ConsultadoAt, bool CoberturaCompleta,
    IReadOnlyList<FuenteAvisosEstado> Fuentes, int TotalAccionesConocidas, int TotalSinLeerConocido,
    int TotalInformativosConocidos, int TotalFiltradoConocido, IReadOnlyList<AvisoCentroItem> Items,
    string? SiguienteContinuacion, NovedadesAvisos Novedades);

public sealed class CentroAvisos(IEnumerable<IFuenteAvisosUsuario> fuentes, IRecibosAvisos recibos, IClock clock, ILogger<CentroAvisos> logger)
{
    private sealed record AvisoConFuente(IFuenteAvisosUsuario Fuente, AvisoUsuario Aviso);
    private sealed record CursorPagina(long Ticks, string Clave, string Contexto);

    public Task<CentroAvisosRespuesta> SincronizarAsync(ContextoAvisos contexto, SincronizarAvisosSolicitud solicitud, CancellationToken ct) =>
        recibos.ConBloqueoAsync(contexto, () => SincronizarDentroAsync(contexto, solicitud, ct), ct);

    private async Task<CentroAvisosRespuesta> SincronizarDentroAsync(ContextoAvisos contexto, SincronizarAvisosSolicitud solicitud, CancellationToken ct)
    {
        ValidarSolicitud(solicitud);
        if (solicitud.Modulo is not null && !fuentes.Any(x => x.Nombre == solicitud.Modulo)) throw Entrada("Módulo desconocido.");
        var cursor = LeerCursor(contexto, solicitud);
        var (todos, estados) = await ConsultarAsync(contexto, ct);
        var avisos = todos.GroupBy(x => x.Aviso.ClaveOcurrencia).Select(g =>
        {
            var elegido = g.OrderBy(x => x.Aviso.Categoria == "Accion" ? 0 : 1).First();
            var lectura = g.Select(x => x.Aviso.LeidoOrigenAt).FirstOrDefault(x => x is not null);
            return elegido with { Aviso = elegido.Aviso with { LeidoOrigenAt = lectura } };
        }).ToList();
        var sincronizados = await recibos.SincronizarAsync(contexto, avisos.Select(x => x.Aviso).ToList(), ct);
        var desde = solicitud.CursorDeteccion is null ? (long?)null : long.Parse(solicitud.CursorDeteccion, CultureInfo.InvariantCulture);
        var nuevos = avisos.Where(x => desde is not null && sincronizados.Recibos[x.Aviso.ClaveOcurrencia].Secuencia > desde &&
            sincronizados.Recibos[x.Aviso.ClaveOcurrencia].LeidoAt is null).ToList();
        var items = avisos.Select(x => new AvisoCentroItem(x.Aviso, sincronizados.Recibos[x.Aviso.ClaveOcurrencia].LeidoAt is not null)).ToList();
        var filtrados = items.Where(x => (solicitud.Modulo is null || x.Aviso.Modulo == solicitud.Modulo) &&
            (solicitud.Condicion != "SinLeer" || !x.Leido) && (solicitud.Condicion != "PorHacer" || x.Aviso.Categoria == "Accion"))
            .OrderByDescending(x => x.Aviso.FechaOcurrencia).ThenBy(x => x.Aviso.ClaveOcurrencia, StringComparer.Ordinal).ToList();
        var pagina = filtrados.Where(x => cursor is null || x.Aviso.FechaOcurrencia.UtcTicks < cursor.Ticks ||
            (x.Aviso.FechaOcurrencia.UtcTicks == cursor.Ticks && string.CompareOrdinal(x.Aviso.ClaveOcurrencia, cursor.Clave) > 0))
            .Take(solicitud.PageSize + 1).ToList();
        string? siguiente = null;
        if (pagina.Count > solicitud.PageSize)
        {
            pagina.RemoveAt(pagina.Count - 1);
            var ultimo = pagina[^1].Aviso;
            siguiente = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new CursorPagina(ultimo.FechaOcurrencia.UtcTicks,
                ultimo.ClaveOcurrencia, ContextoPagina(contexto, solicitud))));
        }
        return new CentroAvisosRespuesta(contexto, clock.UtcNow, estados.All(x => x.Estado == "Complete"), estados,
            items.Count(x => x.Aviso.Categoria == "Accion"), items.Count(x => !x.Leido), items.Count(x => x.Aviso.Categoria == "Informativo"),
            filtrados.Count, pagina, siguiente, new NovedadesAvisos(nuevos.Count > 0, nuevos.Count,
                sincronizados.HastaSecSeguro.ToString(CultureInfo.InvariantCulture), nuevos.GroupBy(x => x.Fuente.Nombre)
                    .Select(x => new NovedadesFuente(x.Key, x.Count())).ToList()));
    }

    public Task<bool> MarcarLeidoAsync(ContextoAvisos contexto, string clave, CancellationToken ct) =>
        recibos.ConBloqueoAsync(contexto, () => LeerDentroAsync(contexto, clave, ct), ct);

    private async Task<bool> LeerDentroAsync(ContextoAvisos contexto, string clave, CancellationToken ct)
    {
        if (clave.Length != 64 || clave.Any(c => !char.IsAsciiHexDigit(c))) throw Entrada("Clave de aviso inválida.");
        var (avisos, _) = await ConsultarAsync(contexto, ct);
        var actuales = avisos.Where(x => x.Aviso.ClaveOcurrencia == clave).ToList();
        if (actuales.Count == 0) return false;
        foreach (var actual in actuales) await actual.Fuente.MarcarLeidoAsync(contexto, actual.Aviso, ct);
        await recibos.SincronizarAsync(contexto, [actuales[0].Aviso], ct);
        await recibos.MarcarLeidoAsync(contexto, clave, ct);
        return true;
    }

    private async Task<(List<AvisoConFuente>, List<FuenteAvisosEstado>)> ConsultarAsync(ContextoAvisos contexto, CancellationToken ct)
    {
        var resultado = new List<AvisoConFuente>();
        var estados = new List<FuenteAvisosEstado>();
        foreach (var fuente in fuentes)
        {
            try
            {
                using var plazo = CancellationTokenSource.CreateLinkedTokenSource(ct);
                plazo.CancelAfter(TimeSpan.FromSeconds(5));
                var datos = await fuente.ConsultarAsync(contexto, plazo.Token);
                var validos = datos.Select(ValidarAviso).ToList();
                resultado.AddRange(validos.Select(x => new AvisoConFuente(fuente, x)));
                estados.Add(new FuenteAvisosEstado(fuente.Nombre, "Complete"));
            }
            catch (Exception ex) when (ex is not ForbiddenException && ex is not UnauthorizedAccessException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "No se pudo consultar la fuente de avisos {Fuente}.", fuente.Nombre);
                estados.Add(new FuenteAvisosEstado(fuente.Nombre, "Failed", "No se pudo consultar este origen. Reintenta."));
            }
        }
        return (resultado, estados);
    }

    private static AvisoUsuario ValidarAviso(AvisoUsuario a)
    {
        if (a.ClaveOcurrencia.Length != 64 || a.ClaveOcurrencia.Any(c => !char.IsAsciiHexDigit(c)) ||
            a.Categoria is not ("Accion" or "Informativo") || string.IsNullOrWhiteSpace(a.Titulo) ||
            a.Destino.Length is 0 or > 300 || !a.Destino.StartsWith('/') || a.Destino.StartsWith("//", StringComparison.Ordinal) ||
            a.Destino.Contains('\\') || a.Destino.Any(char.IsControl))
            throw new InvalidOperationException("La fuente devolvió un aviso inválido.");
        return a with { Titulo = a.Titulo[..Math.Min(a.Titulo.Length, 160)], Motivo = a.Motivo[..Math.Min(a.Motivo.Length, 500)] };
    }

    private static CursorPagina? LeerCursor(ContextoAvisos contexto, SincronizarAvisosSolicitud solicitud)
    {
        if (solicitud.Continuacion is null) return null;
        try
        {
            var c = JsonSerializer.Deserialize<CursorPagina>(Convert.FromBase64String(solicitud.Continuacion));
            if (c is null || c.Ticks < 0 || c.Ticks > DateTimeOffset.MaxValue.UtcTicks || c.Clave is not { Length: 64 } ||
                c.Contexto != ContextoPagina(contexto, solicitud)) throw Entrada("Continuación inválida para este contexto.");
            return c;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw Entrada("Continuación inválida.");
        }
    }

    private static string ContextoPagina(ContextoAvisos c, SincronizarAvisosSolicitud s) =>
        JsonSerializer.Serialize(new[] { c.UsuarioId.ToString(), c.EmpresaId.ToString(), c.SucursalId?.ToString(), s.Modulo, s.Condicion });

    private static void ValidarSolicitud(SincronizarAvisosSolicitud s)
    {
        if (s.PageSize is < 1 or > 50 || s.Continuacion?.Length > 1024 || s.Modulo?.Length > 60 ||
            s.Condicion is not ("Todas" or "SinLeer" or "PorHacer") ||
            (s.CursorDeteccion is not null && (s.CursorDeteccion.Length > 19 || !long.TryParse(s.CursorDeteccion, NumberStyles.None, CultureInfo.InvariantCulture, out _))))
            throw Entrada("Filtros de avisos inválidos.");
    }

    private static BusinessRuleException Entrada(string mensaje) => new("AVISOS_ENTRADA_INVALIDA", mensaje);
}
