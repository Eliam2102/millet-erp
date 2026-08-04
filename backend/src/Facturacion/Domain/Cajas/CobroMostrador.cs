using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Cajas;

/// <summary>Origen del cobro (12-cajas.md §6/§7).</summary>
public enum OrigenCobroMostrador : short
{
    /// <summary>Cobro presencial en mostrador (PUE, anticipo o REPP de mostrador).</summary>
    Mostrador = 1,

    /// <summary>Liquidación de ruta: el chofer cobró en reparto y el cajero captura (`[Decisión 12-7]`).</summary>
    LiquidacionRuta = 2,
}

/// <summary>Estado del cobro.</summary>
public enum EstadoCobroMostrador : short
{
    Registrado = 1,

    /// <summary>Cancelado por supervisor; la reversa vive en movimientos/ajustes, el cobro no se borra.</summary>
    Cancelado = 2,
}

/// <summary>
/// Cobro de mostrador (`[Decisión 12-3]`, 12-cajas.md §6, CAJAS-PR4):
/// desacopla el cobro de la emisión del CFDI. Referencia al
/// <c>Comprobante</c> que lo documenta — <c>FacturaVenta</c>/<c>FacturaAnticipo</c>
/// (PUE) o <c>ReciboPago</c> (PPD cobrado en mostrador). Cada forma de pago
/// genera un <c>caja_movimiento</c> (`[Decisión 12-5]`). Emitir y cobrar son
/// dos comandos (`[Decisión 12-E]`): una factura timbrada sin cobro aparece
/// en la cola "por cobrar" del alcance del cajero.
/// </summary>
public sealed class CobroMostrador : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid CajaSesionId { get; private set; }

    // Dimensiones snapshot del comprobante cobrado (Capa A).
    public Guid SucursalId { get; private set; }
    public short? CanalVentaId { get; private set; }

    /// <summary>Comprobante que documenta el cobro (FK física — mismo esquema).</summary>
    public Guid ComprobanteId { get; private set; }

    public OrigenCobroMostrador Origen { get; private set; }
    public EstadoCobroMostrador Estado { get; private set; }

    /// <summary>Suma de las formas de pago (invariante).</summary>
    public decimal Total { get; private set; }

    /// <summary>Solo MXN en v1 (P6).</summary>
    public string Moneda { get; private set; } = "MXN";

    public DateTimeOffset FechaCobro { get; private set; }
    public Guid UsuarioCobradorId { get; private set; }

    private readonly List<CobroMostradorFormaPago> _formasPago = [];
    public IReadOnlyCollection<CobroMostradorFormaPago> FormasPago => _formasPago.AsReadOnly();

    private CobroMostrador() { }

    private CobroMostrador(
        Guid id, Guid empresaId, Guid cajaSesionId, Guid sucursalId, short? canalVentaId,
        Guid comprobanteId, OrigenCobroMostrador origen, DateTimeOffset fechaCobro,
        Guid usuarioCobradorId) : base(id)
    {
        EmpresaId = empresaId;
        CajaSesionId = cajaSesionId;
        SucursalId = sucursalId;
        CanalVentaId = canalVentaId;
        ComprobanteId = comprobanteId;
        Origen = origen;
        FechaCobro = fechaCobro;
        UsuarioCobradorId = usuarioCobradorId;
        Estado = EstadoCobroMostrador.Registrado;
    }

    /// <summary>
    /// Registra el cobro con sus formas de pago. La validación cross-entity
    /// (sesión abierta, comprobante timbrado y en alcance, no cobrado antes)
    /// vive en el handler.
    /// </summary>
    public static CobroMostrador Registrar(
        Guid empresaId,
        Guid cajaSesionId,
        Guid sucursalId,
        short? canalVentaId,
        Guid comprobanteId,
        OrigenCobroMostrador origen,
        DateTimeOffset fechaCobro,
        Guid usuarioCobradorId,
        IReadOnlyList<(string FormaPago, decimal Importe, string? Referencia, string? CuentaOrdenante, string? CuentaBeneficiaria)> formasPago)
    {
        if (cajaSesionId == Guid.Empty)
            throw new BusinessRuleException("COBRO_SESION_INVALIDA", "La sesión de caja es obligatoria.");
        if (comprobanteId == Guid.Empty)
            throw new BusinessRuleException("COBRO_COMPROBANTE_INVALIDO", "El comprobante es obligatorio.");
        if (usuarioCobradorId == Guid.Empty)
            throw new BusinessRuleException("COBRO_USUARIO_INVALIDO", "El usuario cobrador es obligatorio.");
        if (formasPago.Count == 0)
            throw new BusinessRuleException("COBRO_SIN_FORMAS_PAGO", "El cobro debe llevar al menos una forma de pago.");

        var cobro = new CobroMostrador(
            Guid.CreateVersion7(), empresaId, cajaSesionId, sucursalId, canalVentaId,
            comprobanteId, origen, fechaCobro, usuarioCobradorId);

        foreach (var (formaPago, importe, referencia, ordenante, beneficiaria) in formasPago)
            cobro._formasPago.Add(new CobroMostradorFormaPago(
                Guid.CreateVersion7(), cobro.Id, formaPago, importe, referencia, ordenante, beneficiaria));

        cobro.Total = cobro._formasPago.Sum(f => f.Importe);
        if (cobro.Total <= 0)
            throw new BusinessRuleException("COBRO_TOTAL_INVALIDO", "El total del cobro debe ser mayor que cero.");

        return cobro;
    }

    /// <summary>Cancela el cobro (supervisor). La reversa/ajuste la orquesta el handler ([Decisión 12-C]).</summary>
    public void Cancelar()
    {
        if (Estado != EstadoCobroMostrador.Registrado)
            throw new BusinessRuleException(
                "COBRO_NO_CANCELABLE", $"Solo un cobro Registrado puede cancelarse (actual: {Estado}).");

        Estado = EstadoCobroMostrador.Cancelado;
    }
}

/// <summary>
/// Forma de pago aplicada a un cobro (12-cajas.md §7). Suma = total del cobro
/// (invariante del agregado). Referencia/cuentas para tarjeta/transferencia.
/// </summary>
public sealed class CobroMostradorFormaPago : BaseEntity
{
    public Guid CobroMostradorId { get; private set; }
    public string FormaPago { get; private set; } = string.Empty;
    public decimal Importe { get; private set; }
    public string? Referencia { get; private set; }
    public string? CuentaOrdenante { get; private set; }
    public string? CuentaBeneficiaria { get; private set; }

    private CobroMostradorFormaPago() { }

    internal CobroMostradorFormaPago(
        Guid id, Guid cobroMostradorId, string formaPago, decimal importe,
        string? referencia, string? cuentaOrdenante, string? cuentaBeneficiaria) : base(id)
    {
        if (string.IsNullOrWhiteSpace(formaPago) || formaPago.Trim().Length > 2)
            throw new BusinessRuleException("COBRO_FORMA_PAGO_INVALIDA", "La forma de pago debe ser clave SAT c_FormaPago de 2 caracteres.");
        if (importe <= 0)
            throw new BusinessRuleException("COBRO_IMPORTE_INVALIDO", "El importe de cada forma de pago debe ser mayor que cero.");

        CobroMostradorId = cobroMostradorId;
        FormaPago = formaPago.Trim();
        Importe = importe;
        Referencia = Normalizar(referencia);
        CuentaOrdenante = Normalizar(cuentaOrdenante);
        CuentaBeneficiaria = Normalizar(cuentaBeneficiaria);
    }

    private static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
