using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Pasivos;

/// <summary>
/// Proyección local de <c>cuentas_por_pagar.pasivo.autorizado-para-pago.v1</c>
/// (§4.4 del levantamiento). NO es agregado: la escribe solo el listener
/// de CxP (PR-3, idempotente vía <c>EventoProcesado</c>) y las
/// aplicaciones locales de pago (PR-4); nunca un endpoint. No es fuente
/// de verdad — CxP lo es; el <c>aplicado.v1</c> lo valida CxP
/// (<c>PAGO_EXCEDE_SALDO</c>) y la reversa existe.
///
/// <para>
/// Una fila por <c>FacturaProveedorId</c>; unicidad enforced por índice
/// único (mismo patrón que <c>RecepcionOcLocal</c> de CxP).
/// <c>SaldoPendiente</c> ya viene neto de anticipos y NC aplicadas;
/// <c>MetodoPago</c> es nullable a propósito — hoy no viene en el evento
/// [T-G11], <c>PLATFORM-TODO(&lt;MetodoPagoEnPasivo&gt;)</c>.
/// </para>
/// </summary>
public sealed class PasivoPendientePago : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public Guid FacturaProveedorId { get; private set; }
    public Guid ProveedorId { get; private set; }
    public Guid? OrdenCompraId { get; private set; }
    public decimal MontoTotal { get; private set; }
    public decimal SaldoPendiente { get; private set; }
    public string Moneda { get; private set; } = default!;
    public decimal? TipoCambio { get; private set; }
    public DateOnly FechaVencimiento { get; private set; }
    public Guid? UuidCfdi { get; private set; }
    public string? FolioProveedor { get; private set; }

    // TES-PR8 [T-G11, decisión (a)]: PUE/PPD llega en el evento de CxP como
    // extensión aditiva (cerró PLATFORM-TODO(<MetodoPagoEnPasivo>)).
    // Nullable: eventos previos a la extensión y facturas sin CFDI ligado —
    // el read model de REPP pendientes los muestra como "sin dato".
    public string? MetodoPago { get; private set; }

    // GI-PR2 (doc 12 §D1-b): pasivos INTERNOS (reposición de caja chica,
    // préstamo de viáticos). Para internos, FacturaProveedorId almacena el
    // OrigenId (único por diseño) y ProveedorId el BeneficiarioId — así la
    // clave física y el índice único no cambian; la semántica vive en
    // estos campos. El pago de internos entra en GI-PR3 (guard
    // PAGO_PASIVO_INTERNO mientras tanto).
    public string TipoBeneficiario { get; private set; } = BeneficiarioProveedor;
    public Guid? BeneficiarioId { get; private set; }
    public string OrigenTipo { get; private set; } = OrigenFactura;
    public Guid OrigenId { get; private set; }

    public const string BeneficiarioProveedor = "Proveedor";
    public const string OrigenFactura = "Factura";

    /// <summary>True si el beneficiario no es un proveedor (empleado / caja de sucursal).</summary>
    public bool EsInterno => TipoBeneficiario != BeneficiarioProveedor;

    public DateTimeOffset RecibidoEn { get; private set; }

    private PasivoPendientePago() { }

    public PasivoPendientePago(
        Guid empresaId,
        Guid facturaProveedorId,
        Guid proveedorId,
        Guid? ordenCompraId,
        decimal montoTotal,
        decimal saldoPendiente,
        string moneda,
        decimal? tipoCambio,
        DateOnly fechaVencimiento,
        Guid? uuidCfdi,
        string? folioProveedor,
        DateTimeOffset recibidoEn,
        string? metodoPago = null) : base(Guid.CreateVersion7())
    {
        if (facturaProveedorId == Guid.Empty)
            throw new BusinessRuleException("PASIVO_FACTURA_VACIA", "FacturaProveedorId es obligatorio.");
        if (proveedorId == Guid.Empty)
            throw new BusinessRuleException("PASIVO_PROVEEDOR_VACIO", "ProveedorId es obligatorio.");

        EmpresaId = empresaId;
        FacturaProveedorId = facturaProveedorId;
        ProveedorId = proveedorId;
        OrdenCompraId = ordenCompraId;
        MontoTotal = montoTotal;
        SaldoPendiente = saldoPendiente;
        Moneda = moneda;
        TipoCambio = tipoCambio;
        FechaVencimiento = fechaVencimiento;
        UuidCfdi = uuidCfdi;
        FolioProveedor = folioProveedor;
        RecibidoEn = recibidoEn;
        MetodoPago = metodoPago;
        TipoBeneficiario = BeneficiarioProveedor;
        BeneficiarioId = proveedorId;
        OrigenTipo = OrigenFactura;
        OrigenId = facturaProveedorId;
    }

    /// <summary>
    /// Factory de pasivos INTERNOS (GI-PR2, doc 12): reposición de caja
    /// chica o préstamo de viáticos. <c>FacturaProveedorId</c> almacena el
    /// <paramref name="origenId"/> (reposición / solicitud, único) y
    /// <c>ProveedorId</c> el beneficiario, para no tocar la clave física.
    /// </summary>
    public static PasivoPendientePago CrearInterno(
        Guid empresaId,
        string tipoBeneficiario,
        Guid beneficiarioId,
        string origenTipo,
        Guid origenId,
        decimal montoTotal,
        decimal saldoPendiente,
        string moneda,
        DateOnly fechaVencimiento,
        DateTimeOffset recibidoEn)
    {
        if (string.IsNullOrWhiteSpace(tipoBeneficiario) || tipoBeneficiario == BeneficiarioProveedor)
            throw new BusinessRuleException("PASIVO_INTERNO_TIPO_INVALIDO",
                "Un pasivo interno requiere un tipo de beneficiario distinto a Proveedor.");
        if (beneficiarioId == Guid.Empty)
            throw new BusinessRuleException("PASIVO_INTERNO_BENEFICIARIO_VACIO",
                "El beneficiario del pasivo interno es obligatorio.");
        if (origenId == Guid.Empty || string.IsNullOrWhiteSpace(origenTipo))
            throw new BusinessRuleException("PASIVO_INTERNO_ORIGEN_VACIO",
                "El origen del pasivo interno es obligatorio.");

        var pasivo = new PasivoPendientePago(
            empresaId: empresaId,
            facturaProveedorId: origenId,
            proveedorId: beneficiarioId,
            ordenCompraId: null,
            montoTotal: montoTotal,
            saldoPendiente: saldoPendiente,
            moneda: moneda,
            tipoCambio: null,
            fechaVencimiento: fechaVencimiento,
            uuidCfdi: null,
            folioProveedor: null,
            recibidoEn: recibidoEn)
        {
            TipoBeneficiario = tipoBeneficiario,
            BeneficiarioId = beneficiarioId,
            OrigenTipo = origenTipo,
            OrigenId = origenId,
        };
        return pasivo;
    }

    /// <summary>
    /// Descuenta el saldo local al aplicar un pago (TES-PR4). Validación
    /// LOCAL — la autoridad final es CxP, que rechaza con
    /// <c>PAGO_EXCEDE_SALDO</c> (§4.2); aquí se corta antes para devolver
    /// un 422 legible.
    /// </summary>
    public void AplicarPago(decimal importe)
    {
        if (importe <= 0)
            throw new BusinessRuleException("PASIVO_IMPORTE_INVALIDO",
                "El importe a aplicar debe ser mayor a cero.");
        if (importe > SaldoPendiente)
            throw new BusinessRuleException("PAGO_EXCEDE_SALDO",
                $"El importe {importe:0.00} excede el saldo pendiente {SaldoPendiente:0.00} del pasivo.");
        SaldoPendiente -= importe;
    }

    /// <summary>
    /// Restaura saldo local al revertir un pago (TES-PR4, RN-10). Tope en
    /// <see cref="MontoTotal"/>: si CxP re-emite el pasivo, la
    /// re-proyección corrige cualquier divergencia.
    /// </summary>
    public void RevertirPago(decimal importe)
    {
        if (importe <= 0)
            throw new BusinessRuleException("PASIVO_IMPORTE_INVALIDO",
                "El importe a revertir debe ser mayor a cero.");
        SaldoPendiente = Math.Min(MontoTotal, SaldoPendiente + importe);
    }

    /// <summary>
    /// Re-proyección desde una re-emisión del evento (TES-PR3): un pasivo
    /// puede volver a autorizarse tras una reversa de pago en CxP — el
    /// evento trae el estado vigente (saldo neto, vencimiento) y la
    /// proyección lo adopta tal cual. CxP es la fuente de verdad.
    /// </summary>
    public void ActualizarDesdeEvento(
        decimal montoTotal,
        decimal saldoPendiente,
        string moneda,
        decimal? tipoCambio,
        DateOnly fechaVencimiento,
        Guid? uuidCfdi,
        string? folioProveedor,
        DateTimeOffset recibidoEn,
        string? metodoPago = null)
    {
        MontoTotal = montoTotal;
        SaldoPendiente = saldoPendiente;
        Moneda = moneda;
        TipoCambio = tipoCambio;
        FechaVencimiento = fechaVencimiento;
        UuidCfdi = uuidCfdi;
        FolioProveedor = folioProveedor;
        RecibidoEn = recibidoEn;
        // Una re-emisión sin el dato no borra el conocido.
        if (metodoPago is not null) MetodoPago = metodoPago;
    }
}
