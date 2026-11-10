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

    /// <summary>
    /// M-209 (CA4): base e IVA trasladado por tratamiento (TASA16, TASA8, TASA0, EXENTO, NOOBJETO) leídos del XML guardado del CFDI.
    /// Null si el UUID no está en el buzón o su XML no está disponible; lista vacía si el XML no desglosa traslados por concepto.
    /// </summary>
    Task<IReadOnlyList<TrasladoIvaPublico>?> LeerTrasladosIvaAsync(string uuid, CancellationToken ct);
}

/// <summary>M-209: base e IVA trasladado de un tratamiento del CFDI recibido.</summary>
public sealed record TrasladoIvaPublico(string Tratamiento, decimal Base, decimal Iva);

public sealed record CfdiRecibidoPublico(
    Guid Id, string Uuid, string RfcEmisor, string? Serie, string? Folio, DateTimeOffset FechaCfdi,
    decimal Subtotal, decimal ImpuestosTrasladados, decimal Total, string Moneda, string Tipo, string Estado, decimal? TipoCambio = null);
