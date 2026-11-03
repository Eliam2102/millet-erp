using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Millet.Compartido.Application.Ports;

namespace Millet.Compartido.Infrastructure.Banxico;

/// <summary>
/// K10.6: series del SIE de Banxico por la API REST
/// (<c>series/{claves}/datos/{desde}/{hasta}</c>, token en el encabezado <c>Bmx-Token</c>).
/// Las fechas vienen como <c>dd/MM/yyyy</c> y los días sin dato como «N/E».
/// </summary>
public sealed class BanxicoSieHttpAdapter(IHttpClientFactory clientes, IOptions<BanxicoOptions> options) : IBanxicoSiePort
{
    public const string HttpClientName = "banxico-sie";

    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, decimal>>> ObtenerSeriesAsync(
        IReadOnlyCollection<string> series, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken)
    {
        var cliente = clientes.CreateClient(HttpClientName);
        using var solicitud = new HttpRequestMessage(HttpMethod.Get,
            $"series/{string.Join(',', series)}/datos/{desde:yyyy-MM-dd}/{hasta:yyyy-MM-dd}");
        solicitud.Headers.Add("Bmx-Token", options.Value.Token);
        solicitud.Headers.Accept.ParseAdd("application/json");

        using var respuesta = await cliente.SendAsync(solicitud, cancellationToken);
        respuesta.EnsureSuccessStatusCode();
        await using var cuerpo = await respuesta.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(cuerpo, cancellationToken: cancellationToken);
        return Leer(json.RootElement);
    }

    /// <summary>Convierte <c>{"bmx":{"series":[{"idSerie","datos":[{"fecha","dato"}]}]}}</c>.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, decimal>> Leer(JsonElement raiz)
    {
        var resultado = new Dictionary<string, IReadOnlyDictionary<DateOnly, decimal>>();
        foreach (var serie in raiz.GetProperty("bmx").GetProperty("series").EnumerateArray())
        {
            var valores = new Dictionary<DateOnly, decimal>();
            if (serie.TryGetProperty("datos", out var datos))
            {
                foreach (var d in datos.EnumerateArray())
                {
                    var dato = d.GetProperty("dato").GetString()?.Replace(",", "");
                    if (!decimal.TryParse(dato, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor)) continue;
                    valores[DateOnly.ParseExact(d.GetProperty("fecha").GetString()!, "dd/MM/yyyy", CultureInfo.InvariantCulture)] = valor;
                }
            }
            resultado[serie.GetProperty("idSerie").GetString()!] = valores;
        }
        return resultado;
    }
}
