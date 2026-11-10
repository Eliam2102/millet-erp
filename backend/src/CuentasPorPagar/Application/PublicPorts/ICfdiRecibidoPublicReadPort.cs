namespace Millet.CuentasPorPagar.Application.PublicPorts;

/// <summary>
/// Lectura pública del buzón de CFDI recibidos para otros módulos (86e3xt2q8:
/// Caja elige la factura mensual del banco por las comisiones de la terminal).
/// </summary>
public interface ICfdiRecibidoPublicReadPort
{
    /// <summary>CFDI de ingreso por procesar con fecha en [desde, hasta), opcionalmente de un RFC emisor.</summary>
    Task<IReadOnlyList<CfdiRecibidoPublico>> ListarIngresosPorProcesarAsync(
        DateTimeOffset desde, DateTimeOffset hasta, string? rfcEmisor, int limite, CancellationToken ct);

    Task<CfdiRecibidoPublico?> ObtenerAsync(Guid id, CancellationToken ct);

    /// <summary>K10.8: CFDI recibidos por UUID (los que no están en el buzón no aparecen); UUID con o sin mayúsculas.</summary>
    Task<IReadOnlyList<CfdiRecibidoPublico>> ListarPorUuidsAsync(IReadOnlyCollection<string> uuids, CancellationToken ct);
}

public sealed record CfdiRecibidoPublico(
    Guid Id, string Uuid, string RfcEmisor, string? Serie, string? Folio, DateTimeOffset FechaCfdi,
    decimal Subtotal, decimal ImpuestosTrasladados, decimal Total, string Moneda, string Tipo, string Estado, decimal? TipoCambio = null);
