namespace Millet.Contabilidad.Application.Ports;

/// <summary>
/// K10.7 · puerto de CONSUMIDOR: Contabilidad lee de CxP y de Facturación lo que necesita la marca de cumplimiento de cada documento
/// con IVA (método de pago y si ya tiene su REP), sin tocar sus esquemas. Lo compone <c>Millet.Api</c> con los puertos públicos de
/// los dueños del dato.
/// </summary>
public interface ICumplimientoIvaReadPort
{
    /// <summary>Facturas de proveedor por id: método de pago del CFDI y si CxP ya registró el REP del proveedor.</summary>
    Task<IReadOnlyDictionary<Guid, CumplimientoDocumentoIva>> FacturasProveedorAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>Facturas de venta por id: si Facturación ya timbró un REP que las incluya.</summary>
    Task<IReadOnlyDictionary<Guid, CumplimientoDocumentoIva>> FacturasVentaAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

/// <summary>Método de pago SAT (PUE/PPD; null si no se conoce) y si el documento ya tiene su complemento de pago.</summary>
public sealed record CumplimientoDocumentoIva(string? MetodoPago, bool TieneRep);
