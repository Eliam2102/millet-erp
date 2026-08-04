namespace Millet.Facturacion.Domain.Comprobantes;

/// <summary>
/// Relación CFDI de un <see cref="Comprobante"/> con otro comprobante por su UUID
/// (nodo <c>CfdiRelacionados</c> del CFDI 4.0; §4.4 levantamiento). Entidad hija
/// del comprobante: una factura final con anticipos lleva relaciones 07 a las
/// facturas de anticipo; una NC de amortización relaciona la factura de anticipo
/// <b>y</b> la factura final.
/// </summary>
public sealed class RelacionCfdi
{
    public Guid Id { get; private set; }
    public Guid ComprobanteId { get; private set; }

    /// <summary>UUID del comprobante relacionado.</summary>
    public string UuidRelacionado { get; private set; } = string.Empty;

    /// <summary>Clave SAT del tipo de relación (<c>c_TipoRelacion</c>): 07, 01, 03, 04…</summary>
    public string TipoRelacion { get; private set; } = string.Empty;

    private RelacionCfdi() { }

    internal RelacionCfdi(Guid id, Guid comprobanteId, string uuidRelacionado, string tipoRelacion)
    {
        Id = id;
        ComprobanteId = comprobanteId;
        UuidRelacionado = uuidRelacionado;
        TipoRelacion = tipoRelacion;
    }
}
