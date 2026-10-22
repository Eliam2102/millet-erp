using System.Text.Json;
using Millet.Facturacion.Domain.Proformas;

namespace Millet.Facturacion.Application.Proformas;

/// <summary>
/// Proforma de un pedido (U1.8a). <see cref="Version"/> es el número de versión
/// de la proforma dentro del pedido (1..n), no el token de concurrencia.
/// <see cref="Avisos"/> lista los datos fiscales incompletos al generarla.
/// </summary>
public sealed record ProformaPedidoResponse(
    Guid Id,
    Guid PedidoFacturableId,
    string Folio,
    int Version,
    DateTimeOffset GeneradaEn,
    string? GeneradaPor,
    string? NumeroPedido,
    ProformaCfdi Contenido,
    IReadOnlyList<string> Avisos)
{
    public static ProformaPedidoResponse Desde(ProformaPedido p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new(p.Id, p.PedidoFacturableId, p.Folio, p.NumeroVersion, p.GeneradaEn, p.GeneradaPorNombre,
            p.NumeroPedido, ProformaJson.LeerContenido(p.ContenidoJson), ProformaJson.LeerAvisos(p.AvisosJson));
    }
}

/// <summary>Serialización estable (camelCase) del contenido y avisos guardados en jsonb.</summary>
internal static class ProformaJson
{
    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);

    public static string Escribir<T>(T valor) => JsonSerializer.Serialize(valor, Opciones);

    public static ProformaCfdi LeerContenido(string json) =>
        JsonSerializer.Deserialize<ProformaCfdi>(json, Opciones)
        ?? throw new InvalidOperationException("Contenido de proforma vacío.");

    public static IReadOnlyList<string> LeerAvisos(string json) =>
        JsonSerializer.Deserialize<List<string>>(json, Opciones) ?? [];
}
