using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.AnticipoProveedor;

/// <summary>
/// Pago a un proveedor antes de recibir la factura final (§4.5 del
/// 00-levantamiento). CFDI con serie <c>FANT</c> emitido por el
/// proveedor que documenta el anticipo. Se amortiza contra facturas
/// finales vía <c>AplicarAnticipoAFacturaCommand</c>.
/// </summary>
public sealed class AnticipoProveedor : BaseEntity, IPerteneceAEmpresa, IFiscalmenteRelevante
{
    /// <summary>
    /// Serie estándar del CFDI de anticipo del proveedor.
    /// </summary>
    public const string SerieEstandar = "FANT";

    public Guid EmpresaId { get; set; }

    /// <summary>FK al CFDI origen en CxP.</summary>
    public Guid? CfdiRecibidoId { get; private set; }

    public string UuidCfdi { get; private set; } = default!;
    public Guid ProveedorId { get; private set; }

    /// <summary>Serie del CFDI — debe ser <see cref="SerieEstandar"/> (FANT).</summary>
    public string Serie { get; private set; } = SerieEstandar;
    public string? FolioProveedor { get; private set; }

    public DateTimeOffset FechaCfdi { get; private set; }
    public string Moneda { get; private set; } = "MXN";
    public decimal? TipoCambio { get; private set; }

    public decimal MontoEntregado { get; private set; }
    public decimal MontoAmortizado { get; private set; }

    /// <summary>Saldo amortizable = MontoEntregado - MontoAmortizado. Derivado.</summary>
    public decimal SaldoAmortizable => MontoEntregado - MontoAmortizado;

    public Guid? OrdenCompraId { get; private set; }

    public EstadoAnticipo Estado { get; private set; }

    public DateTimeOffset FechaCaptura { get; private set; }
    public DateTimeOffset? FechaAmortizacion { get; private set; }
    public DateTimeOffset? FechaCancelacion { get; private set; }
    public string? MotivoCancelacion { get; private set; }

    public Guid? CapturadoPor { get; private set; }

    private AnticipoProveedor() { }

    public static AnticipoProveedor Capturar(
        Guid empresaId,
        Guid? cfdiRecibidoId,
        string uuidCfdi,
        Guid proveedorId,
        string serie,
        string? folioProveedor,
        DateTimeOffset fechaCfdi,
        string moneda,
        decimal? tipoCambio,
        decimal montoEntregado,
        Guid? ordenCompraId,
        Guid? capturadoPor,
        DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(uuidCfdi))
            throw new BusinessRuleException("ANTICIPO_UUID_VACIO", "El UUID del CFDI es obligatorio.");
        if (montoEntregado <= 0)
            throw new BusinessRuleException("ANTICIPO_MONTO_INVALIDO",
                "El monto del anticipo debe ser > 0.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("ANTICIPO_MONEDA_INVALIDA",
                "La moneda debe ser código ISO 4217 de 3 letras.");
        if (!string.Equals(serie?.Trim(), SerieEstandar, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                "ANTICIPO_SERIE_INVALIDA",
                $"La serie del CFDI de anticipo debe ser '{SerieEstandar}' (recibida: '{serie}').");
        }

        return new AnticipoProveedor
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            CfdiRecibidoId = cfdiRecibidoId,
            UuidCfdi = uuidCfdi.Trim().ToUpperInvariant(),
            ProveedorId = proveedorId,
            Serie = SerieEstandar,
            FolioProveedor = folioProveedor,
            FechaCfdi = fechaCfdi,
            Moneda = moneda.ToUpperInvariant(),
            TipoCambio = tipoCambio,
            MontoEntregado = montoEntregado,
            MontoAmortizado = 0m,
            OrdenCompraId = ordenCompraId,
            Estado = EstadoAnticipo.Abierto,
            FechaCaptura = ahora,
            CapturadoPor = capturadoPor,
        };
    }

    /// <summary>
    /// Amortiza una porción del anticipo contra una factura final
    /// (§7.1 — AplicarAnticipoAFacturaCommand). El handler valida que
    /// la suma no exceda <see cref="SaldoAmortizable"/>.
    /// </summary>
    public void Amortizar(decimal monto, DateTimeOffset ahora)
    {
        if (Estado == EstadoAnticipo.Cancelado)
        {
            throw new BusinessRuleException(
                "ANTICIPO_CANCELADO",
                "No se puede amortizar un anticipo cancelado.");
        }
        if (monto <= 0)
        {
            throw new BusinessRuleException(
                "ANTICIPO_MONTO_AMORTIZAR_INVALIDO",
                "El monto a amortizar debe ser > 0.");
        }
        if (monto > SaldoAmortizable)
        {
            throw new BusinessRuleException(
                "ANTICIPO_SALDO_INSUFICIENTE",
                $"El monto a amortizar ({monto}) excede el saldo amortizable ({SaldoAmortizable}).");
        }

        MontoAmortizado += monto;
        if (MontoAmortizado >= MontoEntregado)
        {
            Estado = EstadoAnticipo.Amortizado;
            FechaAmortizacion = ahora;
        }
    }

    public void Cancelar(string motivo, DateTimeOffset ahora)
    {
        if (Estado == EstadoAnticipo.Amortizado)
        {
            throw new BusinessRuleException(
                "ANTICIPO_AMORTIZADO_NO_CANCELABLE",
                "Un anticipo totalmente amortizado no puede cancelarse.");
        }
        if (MontoAmortizado > 0)
        {
            throw new BusinessRuleException(
                "ANTICIPO_CON_AMORTIZACION_PARCIAL",
                "Un anticipo con amortización parcial requiere revertir las amortizaciones antes de cancelar.");
        }
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("ANTICIPO_MOTIVO_VACIO", "El motivo de cancelación es obligatorio.");

        Estado = EstadoAnticipo.Cancelado;
        FechaCancelacion = ahora;
        MotivoCancelacion = motivo.Trim();
    }
}
