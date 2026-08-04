using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.ComprobacionGastos;

/// <summary>
/// Línea de una <see cref="ComprobacionGastos"/> — referencia a una
/// <c>FacturaProveedor</c> generada al capturar el CFDI (§7.4.1 paso 4).
///
/// <para>
/// Cada CFDI individual de la comprobación se registra como
/// <c>FacturaProveedor</c> (con OC null) para que afecte gasto, IVA y
/// DIOT en forma independiente. La línea solo guarda el FK + montos
/// snapshot para mostrar en la pantalla maestro-detalle sin necesidad
/// de re-traer la factura.
/// </para>
/// </summary>
public sealed class LineaComprobacionGastos : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid ComprobacionGastosId { get; private set; }

    /// <summary>FK a la <c>FacturaProveedor</c> generada por esta línea.</summary>
    public Guid FacturaProveedorId { get; private set; }

    /// <summary>FK al <c>CfdiRecibido</c> origen (puede ser null si la línea es un ticket fiscal sin CFDI — fuera del MVP F7-PR1).</summary>
    public Guid? CfdiRecibidoId { get; private set; }

    public string? UuidCfdi { get; private set; }
    public Guid ProveedorId { get; private set; }
    public string? FolioProveedor { get; private set; }
    public DateTimeOffset FechaCfdi { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal ImpuestosTrasladados { get; private set; }
    public decimal Retenciones { get; private set; }
    public decimal Total { get; private set; }
    public string Moneda { get; private set; } = "MXN";

    /// <summary>Concepto contable libre — categoría del gasto (papelería, mensajería, etc.).</summary>
    public string? Concepto { get; private set; }

    private LineaComprobacionGastos() { }

    internal LineaComprobacionGastos(
        Guid id,
        Guid empresaId,
        Guid comprobacionGastosId,
        Guid facturaProveedorId,
        Guid? cfdiRecibidoId,
        string? uuidCfdi,
        Guid proveedorId,
        string? folioProveedor,
        DateTimeOffset fechaCfdi,
        decimal subtotal,
        decimal impuestosTrasladados,
        decimal retenciones,
        decimal total,
        string moneda,
        string? concepto) : base(id)
    {
        if (facturaProveedorId == Guid.Empty)
            throw new BusinessRuleException("COMP_LINEA_FACTURA_VACIA",
                "La línea de comprobación requiere FK a la factura generada.");
        if (total <= 0)
            throw new BusinessRuleException("COMP_LINEA_TOTAL_INVALIDO",
                "El total de la línea debe ser > 0.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("COMP_LINEA_MONEDA_INVALIDA",
                "La moneda debe ser código ISO 4217 de 3 letras.");

        EmpresaId = empresaId;
        ComprobacionGastosId = comprobacionGastosId;
        FacturaProveedorId = facturaProveedorId;
        CfdiRecibidoId = cfdiRecibidoId;
        UuidCfdi = uuidCfdi;
        ProveedorId = proveedorId;
        FolioProveedor = folioProveedor;
        FechaCfdi = fechaCfdi;
        Subtotal = subtotal;
        ImpuestosTrasladados = impuestosTrasladados;
        Retenciones = retenciones;
        Total = total;
        Moneda = moneda.ToUpperInvariant();
        Concepto = concepto;
    }
}
