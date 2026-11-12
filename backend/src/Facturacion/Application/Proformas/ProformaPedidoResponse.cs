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
    IReadOnlyList<string> Avisos,
    DateOnly? VigenteHasta = null,
    bool Vencida = false,
    ProformaConformidadResponse? Conformidad = null)
{
    public static ProformaPedidoResponse Desde(
        ProformaPedido p, DateTimeOffset? ahoraUtc = null, ProformaConformidad? conformidad = null)
    {
        ArgumentNullException.ThrowIfNull(p);
        var hoy = Timbrado.CfdiEmisionBuilder.DiaFiscal(ahoraUtc ?? DateTimeOffset.UtcNow);
        return new(p.Id, p.PedidoFacturableId, p.Folio, p.NumeroVersion, p.GeneradaEn, p.GeneradaPorNombre,
            p.NumeroPedido, ProformaJson.LeerContenido(p.ContenidoJson), ProformaJson.LeerAvisos(p.AvisosJson),
            p.VigenteHasta, p.VencidaEl(hoy),
            conformidad is null ? null : ProformaConformidadResponse.Desde(conformidad));
    }
}

/// <summary>M-234: conformidad del cliente con una versión de la proforma.</summary>
public sealed record ProformaConformidadResponse(
    Guid Id,
    Guid ProformaId,
    string Quien,
    DateOnly Fecha,
    string Medio,
    string? Notas,
    string? RegistradaPor,
    DateTimeOffset RegistradaEn,
    bool Invalidada = false,
    string? InvalidadaMotivo = null)
{
    public static ProformaConformidadResponse Desde(ProformaConformidad c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new(c.Id, c.ProformaPedidoId, c.Quien, c.Fecha, c.Medio.ToString(), c.Notas, c.RegistradaPorNombre, c.RegistradaEn, c.Invalidada, c.InvalidadaMotivo);
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
