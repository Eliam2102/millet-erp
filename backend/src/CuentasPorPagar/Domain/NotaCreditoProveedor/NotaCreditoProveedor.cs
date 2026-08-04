using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;

/// <summary>
/// CFDI Egreso emitido por el proveedor que corrige / descuenta /
/// devuelve sobre una factura previa (§4.4 del 00-levantamiento).
/// Agregado raíz independiente de <c>FacturaProveedor</c> — su ciclo
/// es propio: captura, espera de match (A19), aplicación al saldo
/// (F6-PR2).
///
/// <para>
/// **A19 — NC pre-factura como EnEspera**: si llega la NC antes que
/// la factura origen (caso operativamente raro pero válido), el
/// <see cref="UuidRelacionCfdi"/> contiene el UUID de la factura
/// "que va a llegar" pero <see cref="FacturaOrigenId"/> es null.
/// El worker <c>NotaCreditoEnEsperaMatchWorker</c> intenta el match
/// diario contra facturas nuevas del proveedor.
/// </para>
/// </summary>
public sealed class NotaCreditoProveedor : BaseEntity, IPerteneceAEmpresa, IFiscalmenteRelevante
{
    public Guid EmpresaId { get; set; }

    /// <summary>FK al CFDI origen en CxP (NC del proveedor).</summary>
    public Guid? CfdiRecibidoId { get; private set; }

    public string UuidCfdi { get; private set; } = default!;
    public Guid ProveedorId { get; private set; }

    public string? FolioProveedor { get; private set; }
    public string? SerieProveedor { get; private set; }

    public DateTimeOffset FechaCfdi { get; private set; }
    public string Moneda { get; private set; } = "MXN";
    public decimal? TipoCambio { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal ImpuestosTrasladados { get; private set; }
    public decimal Retenciones { get; private set; }
    public decimal Total { get; private set; }

    public TipoNotaCredito Tipo { get; private set; }
    public TipoRelacionCfdi TipoRelacionCfdi { get; private set; }

    /// <summary>UUID de la factura origen (heredado del XML del CFDI Egreso). Siempre poblado.</summary>
    public string UuidRelacionCfdi { get; private set; } = default!;

    /// <summary>FK a la factura origen una vez se resuelva. NULL en EnEspera (A19).</summary>
    public Guid? FacturaOrigenId { get; private set; }

    /// <summary>Monto ya aplicado al saldo de la factura origen (F6-PR2). 0 mientras esté Abierta o EnEspera.</summary>
    public decimal MontoAplicado { get; private set; }

    /// <summary>Saldo amortizable derivado: <c>Total - MontoAplicado</c>.</summary>
    public decimal SaldoPorAplicar => Total - MontoAplicado;

    public EstadoNotaCredito Estado { get; private set; }

    public DateTimeOffset FechaCaptura { get; private set; }
    public DateTimeOffset? FechaMatch { get; private set; }
    public DateTimeOffset? FechaCancelacion { get; private set; }
    public string? MotivoCancelacion { get; private set; }

    /// <summary>Auditoría: usuario que capturó.</summary>
    public Guid? CapturadoPor { get; private set; }

    private NotaCreditoProveedor() { }

    private NotaCreditoProveedor(
        Guid id,
        Guid empresaId,
        Guid? cfdiRecibidoId,
        string uuidCfdi,
        Guid proveedorId,
        string? folioProveedor,
        string? serieProveedor,
        DateTimeOffset fechaCfdi,
        string moneda,
        decimal? tipoCambio,
        decimal subtotal,
        decimal impuestosTrasladados,
        decimal retenciones,
        decimal total,
        TipoNotaCredito tipo,
        TipoRelacionCfdi tipoRelacionCfdi,
        string uuidRelacionCfdi,
        Guid? facturaOrigenId,
        EstadoNotaCredito estadoInicial,
        Guid? capturadoPor,
        DateTimeOffset ahora) : base(id)
    {
        EmpresaId = empresaId;
        CfdiRecibidoId = cfdiRecibidoId;
        UuidCfdi = uuidCfdi;
        ProveedorId = proveedorId;
        FolioProveedor = folioProveedor;
        SerieProveedor = serieProveedor;
        FechaCfdi = fechaCfdi;
        Moneda = moneda;
        TipoCambio = tipoCambio;
        Subtotal = subtotal;
        ImpuestosTrasladados = impuestosTrasladados;
        Retenciones = retenciones;
        Total = total;
        Tipo = tipo;
        TipoRelacionCfdi = tipoRelacionCfdi;
        UuidRelacionCfdi = uuidRelacionCfdi;
        FacturaOrigenId = facturaOrigenId;
        Estado = estadoInicial;
        FechaCaptura = ahora;
        FechaMatch = facturaOrigenId is not null ? ahora : null;
        CapturadoPor = capturadoPor;
    }

    /// <summary>
    /// Captura una NC. Si <paramref name="facturaOrigenId"/> resuelve
    /// (la factura ya existe en CxP) nace <see cref="EstadoNotaCredito.Abierta"/>;
    /// si es null queda en <see cref="EstadoNotaCredito.EnEspera"/>
    /// para que el worker la vincule cuando llegue la factura.
    /// </summary>
    public static NotaCreditoProveedor Capturar(
        Guid empresaId,
        Guid? cfdiRecibidoId,
        string uuidCfdi,
        Guid proveedorId,
        string? folioProveedor,
        string? serieProveedor,
        DateTimeOffset fechaCfdi,
        string moneda,
        decimal? tipoCambio,
        decimal subtotal,
        decimal impuestosTrasladados,
        decimal retenciones,
        decimal total,
        TipoNotaCredito tipo,
        TipoRelacionCfdi tipoRelacionCfdi,
        string uuidRelacionCfdi,
        Guid? facturaOrigenId,
        Guid? capturadoPor,
        DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(uuidCfdi))
            throw new BusinessRuleException("NC_UUID_VACIO", "El UUID del CFDI es obligatorio.");
        if (string.IsNullOrWhiteSpace(uuidRelacionCfdi))
            throw new BusinessRuleException("NC_UUID_RELACION_VACIO",
                "El UUID de la factura origen (relación CFDI) es obligatorio — el SAT exige relación tipo 01/03/07.");
        if (total <= 0)
            throw new BusinessRuleException("NC_TOTAL_INVALIDO", "El total de la NC debe ser > 0.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("NC_MONEDA_INVALIDA", "La moneda debe ser código ISO 4217.");

        var estado = facturaOrigenId is not null ? EstadoNotaCredito.Abierta : EstadoNotaCredito.EnEspera;
        return new NotaCreditoProveedor(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            cfdiRecibidoId: cfdiRecibidoId,
            uuidCfdi: uuidCfdi.Trim().ToUpperInvariant(),
            proveedorId: proveedorId,
            folioProveedor: folioProveedor,
            serieProveedor: serieProveedor,
            fechaCfdi: fechaCfdi,
            moneda: moneda.ToUpperInvariant(),
            tipoCambio: tipoCambio,
            subtotal: subtotal,
            impuestosTrasladados: impuestosTrasladados,
            retenciones: retenciones,
            total: total,
            tipo: tipo,
            tipoRelacionCfdi: tipoRelacionCfdi,
            uuidRelacionCfdi: uuidRelacionCfdi.Trim().ToUpperInvariant(),
            facturaOrigenId: facturaOrigenId,
            estadoInicial: estado,
            capturadoPor: capturadoPor,
            ahora: ahora);
    }

    /// <summary>
    /// Vincula una NC en estado <see cref="EstadoNotaCredito.EnEspera"/>
    /// a su factura origen (manual o por el worker). Pasa a
    /// <see cref="EstadoNotaCredito.Abierta"/>.
    /// </summary>
    public void VincularFacturaOrigen(Guid facturaOrigenId, DateTimeOffset ahora)
    {
        if (Estado != EstadoNotaCredito.EnEspera)
        {
            throw new BusinessRuleException(
                "NC_NO_EN_ESPERA",
                $"Solo NCs en EnEspera pueden vincularse a factura (actual: {Estado}).");
        }
        FacturaOrigenId = facturaOrigenId;
        Estado = EstadoNotaCredito.Abierta;
        FechaMatch = ahora;
    }

    /// <summary>
    /// Aplica un monto al saldo de la factura origen (F6-PR2). Solo
    /// desde estado <see cref="EstadoNotaCredito.Abierta"/>. Si la
    /// aplicación amortiza el total, pasa a
    /// <see cref="EstadoNotaCredito.Aplicada"/>.
    /// </summary>
    public void AplicarMonto(decimal monto)
    {
        if (Estado != EstadoNotaCredito.Abierta)
        {
            throw new BusinessRuleException(
                "NC_NO_APLICABLE",
                $"Solo NCs Abiertas pueden aplicarse (actual: {Estado}).");
        }
        if (monto <= 0)
            throw new BusinessRuleException("NC_MONTO_INVALIDO", "El monto debe ser > 0.");
        if (monto > SaldoPorAplicar)
            throw new BusinessRuleException(
                "NC_SALDO_INSUFICIENTE",
                $"El monto a aplicar ({monto}) excede el saldo por aplicar ({SaldoPorAplicar}).");

        MontoAplicado += monto;
        if (MontoAplicado >= Total)
        {
            Estado = EstadoNotaCredito.Aplicada;
        }
    }

    public void Cancelar(string motivo, DateTimeOffset ahora)
    {
        if (Estado is EstadoNotaCredito.Aplicada)
        {
            throw new BusinessRuleException(
                "NC_APLICADA_NO_CANCELABLE",
                "Una NC ya aplicada no se cancela en este flujo; usar reverso (F6-PR2+).");
        }
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("NC_MOTIVO_CANCELACION_VACIO", "El motivo es obligatorio.");

        Estado = EstadoNotaCredito.Cancelada;
        FechaCancelacion = ahora;
        MotivoCancelacion = motivo.Trim();
    }
}
