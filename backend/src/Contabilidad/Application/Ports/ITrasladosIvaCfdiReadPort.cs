namespace Millet.Contabilidad.Application.Ports;

/// <summary>
/// M-209 (86e3zr7mp, CA4) · puerto de CONSUMIDOR: base e IVA por tratamiento del XML de un CFDI recibido, para corregir el IVA de una
/// factura de proveedor contra su documento. Lo compone <c>Millet.Api</c> con el puerto público de CxP; Contabilidad no lee sus tablas.
/// </summary>
public interface ITrasladosIvaCfdiReadPort
{
    /// <summary>Null si el CFDI o su XML no están disponibles; vacío si el XML no desglosa traslados por concepto.</summary>
    Task<IReadOnlyList<TrasladoIvaDocumento>?> LeerAsync(string uuid, CancellationToken ct);
}

/// <summary>Base e IVA de un tratamiento (TASA16, TASA8, TASA0, EXENTO, NOOBJETO) según el XML.</summary>
public sealed record TrasladoIvaDocumento(string Tratamiento, decimal Base, decimal Iva);
