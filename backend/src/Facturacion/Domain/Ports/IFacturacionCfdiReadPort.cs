namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Puerto de lectura que <b>Facturación expone</b> a otros módulos (p.ej. Obras)
/// para consultar los CFDIs emitidos sin tocar su esquema (§7.2, F11). Es un
/// adapter real sobre el propio <c>FacturacionDbContext</c> (a diferencia de los
/// puertos que Facturación consume de otros módulos).
/// </summary>
public interface IFacturacionCfdiReadPort
{
    /// <summary>CFDIs (facturas de venta) vinculados a una Obra.</summary>
    Task<IReadOnlyList<CfdiObraLectura>> ObtenerPorObraAsync(long obraId, CancellationToken cancellationToken);

    /// <summary>K10.8: CFDI emitidos (cualquier tipo de comprobante) por UUID, para las pólizas de la contabilidad electrónica.</summary>
    Task<IReadOnlyList<CfdiEmitidoLectura>> ObtenerPorUuidsAsync(IReadOnlyCollection<string> uuids, CancellationToken cancellationToken);
}

/// <summary>Datos del CFDI emitido que pide el nodo <c>CompNal</c>: RFC del receptor, total, moneda y tipo de cambio.</summary>
public sealed record CfdiEmitidoLectura(string Uuid, string ReceptorRfc, decimal Total, string Moneda, decimal? TipoCambio);

/// <summary>Snapshot de un CFDI emitido para consultas externas por obra.</summary>
public sealed record CfdiObraLectura(
    Guid ComprobanteId,
    string Folio,
    string? Uuid,
    decimal Total,
    string Moneda,
    string Estado,
    DateTimeOffset? FechaTimbrado);
