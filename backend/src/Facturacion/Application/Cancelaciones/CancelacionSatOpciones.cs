using System.Globalization;
using System.Text.RegularExpressions;
using Millet.Integraciones.Fiscal.Domain.Ports;

namespace Millet.Facturacion.Application.Cancelaciones;

/// <summary>
/// H-U31: el SAT responde 205 («UUID no existe») cuando aún no refleja un CFDI recién timbrado. La cancelación no se
/// rechaza: queda pendiente de reintento y se sugiere cuándo volver a intentarlo. Sección <c>Facturacion:CancelacionSat</c>.
/// </summary>
public sealed class CancelacionSatOpciones
{
    public const string Seccion = "Facturacion:CancelacionSat";

    /// <summary>Minutos para el reintento sugerido: desde el timbrado en el primer envío y desde cada reintento después.</summary>
    public int ReintentoMinutos { get; set; } = 60;

    public TimeSpan Intervalo => TimeSpan.FromMinutes(Math.Clamp(ReintentoMinutos, 1, 7 * 24 * 60));
}

/// <summary>H-U31: reconoce la respuesta 205 del SAT y redacta el aviso para el usuario.</summary>
public static partial class CancelacionSat205
{
    /// <summary>Inicio fijo del aviso: también marca la solicitud como «pendiente de reintento».</summary>
    public const string PrefijoAviso = "El SAT aún no refleja este CFDI (205).";

    [GeneratedRegex(@"(?<!\d)205(?!\d)")]
    private static partial Regex Codigo205();

    public static bool EsNoReflejadoAun(CancelacionResultado r) =>
        r.Estado is CancelacionEstado.Error or CancelacionEstado.Rechazada
        && (Codigo205().IsMatch(r.ErrorMensaje ?? string.Empty) || Codigo205().IsMatch(r.EstatusSat ?? string.Empty));

    /// <summary>Primer envío: timbrado + intervalo (si ya pasó, ahora + intervalo). Reintento: ahora + intervalo.</summary>
    public static DateTimeOffset ReintentoSugerido(DateTimeOffset? fechaTimbrado, DateTimeOffset ahora, TimeSpan intervalo, bool esReintento)
    {
        if (!esReintento && fechaTimbrado is { } t && t + intervalo > ahora) return t + intervalo;
        return ahora + intervalo;
    }

    public static string Aviso(DateTimeOffset reintentoUtc)
    {
        var zona = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
        var local = TimeZoneInfo.ConvertTime(reintentoUtc, zona);
        return $"{PrefijoAviso} Puede tardar; reintenta en unos minutos (el SAT puede demorar hasta 72 h tras el timbrado). " +
            $"Reintento sugerido: {local.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}.";
    }
}
