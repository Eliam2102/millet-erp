using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.Viaticos;

/// <summary>
/// Línea de comprobación de viáticos (§7.4.2 paso 6 del 00-levantamiento,
/// F7-PR3). El empleado sube CFDIs al regresar del viaje; cada uno se
/// modela como una línea con FK opcional a la <c>FacturaProveedor</c>
/// generada por <c>LiberarComprobacionViaticosCommand</c>.
///
/// <para>
/// El FK es opcional porque al capturar la comprobación las facturas
/// aún no existen — se generan cuando CxP libera. Hasta entonces, las
/// líneas son "borradores" con el monto declarado por el empleado.
/// </para>
/// </summary>
public sealed class LineaComprobacionViaticos : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid SolicitudViaticosId { get; private set; }

    public Guid? FacturaProveedorId { get; private set; }
    public Guid? CfdiRecibidoId { get; private set; }
    public string? UuidCfdi { get; private set; }
    public Guid? ProveedorId { get; private set; }
    public string? FolioProveedor { get; private set; }
    public DateTimeOffset FechaGasto { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal ImpuestosTrasladados { get; private set; }
    public decimal Retenciones { get; private set; }
    public decimal Total { get; private set; }
    public string Moneda { get; private set; } = "MXN";

    /// <summary>Concepto libre — "Hospedaje", "Transporte", "Alimentación", etc.</summary>
    public string Concepto { get; private set; } = default!;

    /// <summary>True si la línea es un ticket no fiscal (sin CFDI). Limitado a tope contable; el área lo permite con justificación.</summary>
    public bool EsTicketNoFiscal { get; private set; }

    private LineaComprobacionViaticos() { }

    internal LineaComprobacionViaticos(
        Guid id,
        Guid empresaId,
        Guid solicitudViaticosId,
        Guid? cfdiRecibidoId,
        string? uuidCfdi,
        Guid? proveedorId,
        string? folioProveedor,
        DateTimeOffset fechaGasto,
        decimal subtotal,
        decimal impuestosTrasladados,
        decimal retenciones,
        decimal total,
        string moneda,
        string concepto,
        bool esTicketNoFiscal) : base(id)
    {
        if (total <= 0)
            throw new BusinessRuleException("VIA_LINEA_TOTAL_INVALIDO",
                "El total de la línea debe ser > 0.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("VIA_LINEA_MONEDA_INVALIDA",
                "La moneda debe ser código ISO 4217 de 3 letras.");
        if (string.IsNullOrWhiteSpace(concepto))
            throw new BusinessRuleException("VIA_LINEA_CONCEPTO_VACIO",
                "El concepto es obligatorio.");
        if (!esTicketNoFiscal && string.IsNullOrWhiteSpace(uuidCfdi))
            throw new BusinessRuleException("VIA_LINEA_UUID_REQUERIDO",
                "Líneas fiscales requieren UUID del CFDI.");

        EmpresaId = empresaId;
        SolicitudViaticosId = solicitudViaticosId;
        CfdiRecibidoId = cfdiRecibidoId;
        UuidCfdi = uuidCfdi?.Trim().ToUpperInvariant();
        ProveedorId = proveedorId;
        FolioProveedor = folioProveedor;
        FechaGasto = fechaGasto;
        Subtotal = subtotal;
        ImpuestosTrasladados = impuestosTrasladados;
        Retenciones = retenciones;
        Total = total;
        Moneda = moneda.ToUpperInvariant();
        Concepto = concepto.Trim();
        EsTicketNoFiscal = esTicketNoFiscal;
    }

    /// <summary>
    /// Vincula la línea con la <c>FacturaProveedor</c> generada al
    /// liberar la comprobación (CxP, paso 6 del flujo). Solo aplica a
    /// líneas fiscales.
    /// </summary>
    internal void VincularFactura(Guid facturaProveedorId)
    {
        if (EsTicketNoFiscal)
            throw new BusinessRuleException("VIA_LINEA_TICKET_SIN_FACTURA",
                "Las líneas de ticket no fiscal no generan factura.");
        if (FacturaProveedorId is not null)
            throw new BusinessRuleException("VIA_LINEA_YA_VINCULADA",
                "La línea ya tiene una factura asociada.");

        FacturaProveedorId = facturaProveedorId;
    }
}
