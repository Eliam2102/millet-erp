using System.Globalization;
using System.Text.Json;
using Millet.Almacen.Domain.Ports;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// REQUISICION_ENVIADA (contrato Almacenes v1 §8.1; v9: <c>lineas[].posicion</c> y medidas con decimales): valida la forma de <c>datos</c> y
/// delega la creación a Compras por <see cref="IComprasCrearRqAppPort"/>. El acuse lleva el
/// folio del ERP (obligatorio en este tipo); duplicado por <c>datos.folio</c> → DUPLICADO con
/// el primer folio; cualquier dato inválido o desconocido → ERROR sin crear nada.
/// </summary>
public static class AlmacenesAppRequisicionEnviada
{
    private const int MaxLineas = 500;

    public static async Task<(string Estado, string? Folio, string? Error)> ManejarAsync(
        JsonElement datos, IComprasCrearRqAppPort rqApp, CancellationToken ct, Guid? centroCostoId = null)
    {
        var (solicitud, error) = Parsear(datos);
        if (solicitud is null) return ("ERROR", null, error);
        solicitud = solicitud with { CentroCostoId = centroCostoId };
        var r = await rqApp.CrearAsync(solicitud, ct);
        if (r.Error is not null) return ("ERROR", null, r.Error);
        return (r.Duplicada ? "DUPLICADO" : "RECIBIDO", r.Folio, null);
    }

    public const string Cancelada = "REQUISICION_CANCELADA";
    public const string Reducida = "REQUISICION_REDUCIDA";

    /// <summary>
    /// REQUISICION_CANCELADA / REQUISICION_REDUCIDA (M-446, contrato v8): cancelan o reducen la RQ por su folio de la app (<c>folio</c>)
    /// en Compras, sin mover existencia. Al cancelar una entrada llegan junto con una SALIDA_MANUAL (enlazada por
    /// <c>entradaCancelada</c> / <c>origen.entrada</c> y la compra), que es la que baja lo recibido. La cancelada sin compra (el ERP la
    /// rechazó o no la va a comprar) no trae <c>entradaCancelada</c>; la reducida con <c>cierreConFaltante</c> trae en <c>lineas[]</c>
    /// lo que ya no se espera. Acuse RECIBIDO con el folio del ERP de la RQ (vacío si nunca llegó al ERP).
    /// </summary>
    public static async Task<(string Estado, string? Folio, string? Error)> ManejarAvisoAsync(
        string tipo, JsonElement d, IComprasCrearRqAppPort rqApp, CancellationToken ct)
    {
        var folio = Texto(d, "folio")?.Trim();
        if (string.IsNullOrWhiteSpace(folio) || folio.Length > 64)
            return ("ERROR", null, "datos.folio es requerido (hasta 64 caracteres).");
        var entrada = Texto(d, "entradaCancelada")?.Trim();
        var motivo = string.Join(" · ", new[]
        {
            Texto(d, "motivo")?.Trim(),
            string.IsNullOrEmpty(entrada) ? null : $"entrada {entrada} cancelada",
            Texto(d, "compra")?.Trim() is { Length: > 0 } compra ? $"compra {compra}" : null,
            d.TryGetProperty("cierreConFaltante", out var c) && c.ValueKind == JsonValueKind.True ? "cierre con faltante" : null,
        }.Where(x => !string.IsNullOrEmpty(x)));

        // v9: folioErp (el folio que el ERP acusó) manda para encontrar la RQ; sucursal no hace falta (el folio de la app es único).
        var folioErp = Texto(d, "folioErp")?.Trim() is { Length: > 0 } fe ? fe : null;
        AvisoRqAppResultado r;
        if (tipo == Cancelada)
            r = await rqApp.CancelarAsync(folio, motivo, ct, folioErp);
        else
        {
            if (!d.TryGetProperty("lineas", out var ls) || ls.ValueKind != JsonValueKind.Array || ls.GetArrayLength() == 0
                || ls.GetArrayLength() > MaxLineas)
                return ("ERROR", null, $"datos.lineas debe traer de 1 a {MaxLineas} líneas.");
            var lineas = new List<LineaQuitadaRqApp>();
            var n = 0;
            foreach (var l in ls.EnumerateArray())
            {
                n++;
                if (l.ValueKind != JsonValueKind.Object || !Entero(l, "posicion", out var pos) || pos is null or <= 0
                    || !Entero(l, "piezasQuitadas", out var piezas) || piezas is null or <= 0)
                    return ("ERROR", null, $"Línea {n}: posicion y piezasQuitadas deben ser enteros mayores que cero.");
                lineas.Add(new LineaQuitadaRqApp(pos.Value, piezas.Value));
            }
            r = await rqApp.ReducirAsync(folio, lineas, motivo, ct, folioErp);
        }
        return r.Error is null ? ("RECIBIDO", r.Folio, null) : ("ERROR", r.Folio, r.Error);
    }

    public static (CrearRqAppSolicitud?, string?) Parsear(JsonElement d)
    {
        var folio = Texto(d, "folio");
        if (string.IsNullOrWhiteSpace(folio) || folio.Length > 64)
            return (null, "datos.folio es requerido (hasta 64 caracteres).");
        var sucursal = Texto(d, "sucursal");
        if (string.IsNullOrWhiteSpace(sucursal) || sucursal.Length > 50)
            return (null, "datos.sucursal es requerida (código de la sucursal).");
        if (!d.TryGetProperty("proveedor", out var prov) || prov.ValueKind != JsonValueKind.Object
            || !Entero(prov, "claveAw", out var claveAw) || claveAw is null or <= 0)
            return (null, "datos.proveedor.claveAw es requerida y debe ser un entero mayor que cero.");

        DateOnly? fecha = null;
        var fechaTxt = Texto(d, "fechaRequerida");
        if (fechaTxt is not null)
        {
            if (!DateOnly.TryParseExact(fechaTxt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
                return (null, "datos.fechaRequerida debe tener formato AAAA-MM-DD.");
            fecha = f;
        }

        if (!d.TryGetProperty("lineas", out var ls) || ls.ValueKind != JsonValueKind.Array
            || ls.GetArrayLength() == 0 || ls.GetArrayLength() > MaxLineas)
            return (null, $"datos.lineas debe traer de 1 a {MaxLineas} líneas.");
        var lineas = new List<LineaRqApp>();
        var n = 0;
        foreach (var l in ls.EnumerateArray())
        {
            n++;
            if (l.ValueKind != JsonValueKind.Object) return (null, $"Línea {n}: formato inválido.");
            string? art = l.TryGetProperty("articuloAw", out var a)
                ? a.ValueKind == JsonValueKind.Number ? a.GetRawText() : a.ValueKind == JsonValueKind.String ? a.GetString() : null
                : null;
            if (string.IsNullOrWhiteSpace(art) || art.Length > 50)
                return (null, $"Línea {n}: articuloAw es requerido.");
            if (!Entero(l, "piezas", out var piezas) || piezas is null or <= 0)
                return (null, $"Línea {n}: piezas debe ser un entero mayor que cero.");
            if (!Milimetros(l, "ancho", out var ancho) || ancho <= 0 || !Milimetros(l, "alto", out var alto) || alto <= 0)
                return (null, $"Línea {n}: ancho y alto deben ser números mayores que cero o venir vacíos.");
            if (!Entero(l, "posicion", out var posicion) || posicion <= 0)
                return (null, $"Línea {n}: posicion debe ser un entero mayor que cero.");
            // obra (nombre) o, si no viene, obraAw (clave de A+W): el ERP solo guarda un texto de obra por línea.
            var obra = Texto(l, "obra")?.Trim() is { Length: > 0 } o ? o
                : l.TryGetProperty("obraAw", out var oa) && oa.ValueKind is JsonValueKind.Number or JsonValueKind.String ? oa.ToString().Trim() : null;
            lineas.Add(new LineaRqApp(art.Trim(), piezas.Value, ancho, alto, string.IsNullOrEmpty(obra) ? null : obra, posicion));
        }
        var posiciones = lineas.Where(x => x.Posicion is not null).Select(x => x.Posicion!.Value).ToList();
        if (posiciones.Count > 0 && (posiciones.Count != lineas.Count || posiciones.Distinct().Count() != posiciones.Count))
            return (null, "lineas[].posicion debe venir en todas las líneas y sin repetirse.");
        // v11: autorizacion { usuario, fecha } opcional (las enviadas antes del 12-nov no la traen); si viene, completa.
        string? autorizadaPor = null;
        DateTimeOffset? autorizadaEn = null;
        if (d.TryGetProperty("autorizacion", out var au) && au.ValueKind != JsonValueKind.Null)
        {
            autorizadaPor = au.ValueKind == JsonValueKind.Object ? Texto(au, "usuario")?.Trim() : null;
            var fechaAu = au.ValueKind == JsonValueKind.Object ? Texto(au, "fecha") : null;
            if (string.IsNullOrEmpty(autorizadaPor) || autorizadaPor.Length > 100 || fechaAu is null
                || !DateTimeOffset.TryParse(fechaAu, CultureInfo.InvariantCulture, DateTimeStyles.None, out var en))
                return (null, "datos.autorizacion debe traer usuario (hasta 100 caracteres) y fecha con zona horaria.");
            autorizadaEn = en;
        }
        return (new CrearRqAppSolicitud(folio.Trim(), sucursal.Trim(), claveAw.Value, fecha,
            Texto(d, "comentario")?.Trim(), lineas) { AutorizadaPor = autorizadaPor, AutorizadaEn = autorizadaEn }, null);
    }

    private static string? Texto(JsonElement o, string n) =>
        o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// Medida en mm (v9: «sin redondear», puede traer decimales). La línea de la RQ guarda mm enteros: se redondea al mm, con
    /// .5 hacia arriba. Las hojas de A+W vienen en mm enteros (3302.0); la fracción solo pasaría con una medida capturada a mano.
    /// false = presente pero no es número; true con null = ausente o null.
    /// </summary>
    private static bool Milimetros(JsonElement o, string n, out int? valor)
    {
        valor = null;
        if (!o.TryGetProperty(n, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDecimal(out var d) || d is < int.MinValue or > int.MaxValue) return false;
        valor = (int)Math.Round(d, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>false = presente pero no es entero; true con null = ausente o null.</summary>
    private static bool Entero(JsonElement o, string n, out int? valor)
    {
        valor = null;
        if (!o.TryGetProperty(n, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        // La app manda las medidas como 3302.0 (contrato v8, mensajes reales de ENSAYO): vale si no tiene fracción.
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDecimal(out var d) || d != decimal.Truncate(d) || d is < int.MinValue or > int.MaxValue)
            return false;
        valor = (int)d;
        return true;
    }
}
