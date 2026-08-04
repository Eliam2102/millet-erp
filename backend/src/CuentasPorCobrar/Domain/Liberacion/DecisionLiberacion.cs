using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.Liberacion;

/// <summary>
/// Decisión auditada de crédito sobre un pedido A+W (§3.1 del
/// 00-levantamiento, §4.1-4.2 del 01-diseño, CXC-PR4).
///
/// <para>
/// <b>Inmutable una vez emitida</b>: correcciones = nueva decisión (el
/// agregado no expone mutadores). Guarda siempre el snapshot del crédito
/// disponible al momento de decidir — la auditoría vale más que el dato
/// en vivo. <c>LiberadoConOverride</c> exige el <see cref="OverrideId"/>
/// de una <see cref="AutorizacionCredito"/> consumida en la misma
/// transacción (lo garantiza el handler).
/// </para>
///
/// <para>
/// El efecto sobre A+W (write-back a tabla-puente) es un PR dependiente
/// y desacoplado [Decisión CXC-2] — esta decisión solo persiste y emite
/// <c>DecisionLiberacionEmitidaEvent</c>.
/// </para>
/// </summary>
public sealed class DecisionLiberacion : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }

    /// <summary>Folio/referencia del pedido en A+W.</summary>
    public string PedidoRef { get; private set; } = default!;

    public Guid ClienteId { get; private set; }
    public string Moneda { get; private set; } = default!;
    public decimal MontoPedido { get; private set; }

    /// <summary>Foto del crédito disponible al momento de decidir (§4.2).</summary>
    public decimal CreditoDisponibleSnapshot { get; private set; }

    public ResultadoLiberacion Resultado { get; private set; }
    public ReglaAplicadaLiberacion ReglaAplicada { get; private set; }

    /// <summary>Autorización consumible usada, solo en LiberadoConOverride.</summary>
    public Guid? OverrideId { get; private set; }

    public Guid DecididoPor { get; private set; }
    public DateTimeOffset DecididoEn { get; private set; }

    private DecisionLiberacion() { }

    public static DecisionLiberacion Emitir(
        Guid empresaId,
        string pedidoRef,
        Guid clienteId,
        string moneda,
        decimal montoPedido,
        decimal creditoDisponibleSnapshot,
        ResultadoLiberacion resultado,
        ReglaAplicadaLiberacion reglaAplicada,
        Guid? overrideId,
        Guid decididoPor,
        DateTimeOffset decididoEn)
    {
        if (string.IsNullOrWhiteSpace(pedidoRef))
            throw new BusinessRuleException("DL_PEDIDO_VACIO", "La referencia del pedido es obligatoria.");
        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("DL_CLIENTE_VACIO", "El cliente es obligatorio.");
        if (montoPedido <= 0)
            throw new BusinessRuleException("DL_MONTO_INVALIDO", "El monto del pedido debe ser > 0.");
        if (decididoPor == Guid.Empty)
            throw new BusinessRuleException("DL_USUARIO_VACIO", "El usuario que decide es obligatorio.");
        if (resultado == ResultadoLiberacion.LiberadoConOverride && overrideId is null)
            throw new BusinessRuleException("DL_OVERRIDE_REQUERIDO",
                "LiberadoConOverride exige la autorización consumida.");
        if (resultado != ResultadoLiberacion.LiberadoConOverride && overrideId is not null)
            throw new BusinessRuleException("DL_OVERRIDE_SOBRANTE",
                "Solo LiberadoConOverride lleva autorización.");

        return new DecisionLiberacion
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            PedidoRef = pedidoRef.Trim(),
            ClienteId = clienteId,
            Moneda = moneda,
            MontoPedido = montoPedido,
            CreditoDisponibleSnapshot = creditoDisponibleSnapshot,
            Resultado = resultado,
            ReglaAplicada = reglaAplicada,
            OverrideId = overrideId,
            DecididoPor = decididoPor,
            DecididoEn = decididoEn,
        };
    }
}
