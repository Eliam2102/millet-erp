using System.Globalization;
using System.Text.Json;
using Millet.Almacen.Domain.Ports;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// REQUISICION_ENVIADA (contrato Almacenes v1 §8.1): valida la forma de <c>datos</c> y
/// delega la creación a Compras por <see cref="IComprasCrearRqAppPort"/>. El acuse lleva el
/// folio del ERP (obligatorio en este tipo); duplicado por <c>datos.folio</c> → DUPLICADO con
/// el primer folio; cualquier dato inválido o desconocido → ERROR sin crear nada.
/// </summary>
public static class AlmacenesAppRequisicionEnviada
{
    private const int MaxLineas = 500;

    public static async Task<(string Estado, string? Folio, string? Error)> ManejarAsync(
        JsonElement datos, IComprasCrearRqAppPort rqApp, CancellationToken ct)
    {
        var (solicitud, error) = Parsear(datos);
        if (solicitud is null) return ("ERROR", null, error);
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

        AvisoRqAppResultado r;
        if (tipo == Cancelada)
            r = await rqApp.CancelarAsync(folio, motivo, ct);
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
            r = await rqApp.ReducirAsync(folio, lineas, motivo, ct);
        }
        return r.Error is null ? ("RECIBIDO", r.Folio, null) : ("ERROR", r.Folio, r.Error);
    }

    internal static (CrearRqAppSolicitud?, string?) Parsear(JsonElement d)
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
            if (!Entero(l, "ancho", out var ancho) || ancho <= 0 || !Entero(l, "alto", out var alto) || alto <= 0)
                return (null, $"Línea {n}: ancho y alto deben ser enteros mayores que cero o venir vacíos.");
            lineas.Add(new LineaRqApp(art.Trim(), piezas.Value, ancho, alto, Texto(l, "obra")?.Trim()));
        }
        return (new CrearRqAppSolicitud(folio.Trim(), sucursal.Trim(), claveAw.Value, fecha,
            Texto(d, "comentario")?.Trim(), lineas), null);
    }

    private static string? Texto(JsonElement o, string n) =>
        o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>false = presente pero no es entero; true con null = ausente o null.</summary>
    private static bool Entero(JsonElement o, string n, out int? valor)
    {
        valor = null;
        if (!o.TryGetProperty(n, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var i)) return false;
        valor = i;
        return true;
    }
}
