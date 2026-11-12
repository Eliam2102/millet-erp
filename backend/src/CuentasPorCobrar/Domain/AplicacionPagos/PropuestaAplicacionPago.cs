using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.AplicacionPagos;

/// <summary>Estados de una <see cref="PropuestaAplicacionPago"/> (§3.1).</summary>
public enum EstadoPropuestaAplicacion : short
{
    Propuesta = 1,
    Confirmada = 2,
    Rechazada = 3,
}

/// <summary>
/// CXC-06 (86e3qddef): tipo del ajuste documentado de un cobro corto. Define la cuenta de la póliza por rol de Contabilidad
/// (supuesto «a validar»): comisión bancaria → <c>COMISIONES_BANCARIAS</c>; diferencia menor → <c>DIFERENCIAS_EN_COBRO</c>.
/// </summary>
public enum TipoAjusteCobro : short
{
    ComisionBancaria = 1,
    DiferenciaMenor = 2,

    /// <summary>
    /// M-326: redondeo de hasta <see cref="PropuestaAplicacionPago.ToleranciaRedondeo"/> en el cobro, sin documentar ni autorizar.
    /// Lo asigna el sistema (no se elige a mano): rol <c>REDONDEO</c>.
    /// </summary>
    Redondeo = 3,
}

/// <summary>
/// Propuesta de aplicación de pago (§3.1 del 00-levantamiento, §4.1-4.2
/// del 01-diseño, CXC-PR7): matching depósito ↔ facturas construido desde
/// el remittance del cliente. CxC propone; Tesorería confirma o rechaza contra el banco.
/// Su resolución se consume por eventos; CxC no ofrece confirmación directa.
///
/// <para>
/// Invariantes (§4.2): <c>Σ importe_aplicado + ajuste_no_fiscal =
/// monto_deposito - saldo_a_favor_por_identificar</c>; el ajuste es negativo (depósito corto, menor a la tolerancia no fiscal)
/// o, solo si es redondeo (M-326), un excedente positivo de hasta $0.99. La tolerancia no fiscal (parámetro, default $50 USD — gate
/// fiscal pendiente); sin <c>remittance_ref</c> no se puede proponer
/// (regla 2.1). La confirmación NO aplica pagos a cartera — eso lo hace
/// el REPP timbrado vía eventos de Facturación.
/// </para>
/// </summary>
public sealed class PropuestaAplicacionPago : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    /// <summary>
    /// M-326: un faltante de hasta este importe (en la moneda del cobro) es redondeo: va a la cuenta REDONDEO sin motivo; su autorización la da el Gerente de Área.
    /// Supuesto a validar con Contabilidad. Un faltante que se documenta con tipo y motivo (comisión, diferencia menor, M-086) no es redondeo.
    /// </summary>
    public const decimal ToleranciaRedondeo = 0.99m;

    public Guid EmpresaId { get; set; }

    public Guid? PropuestoPor { get; private set; }
    public decimal SaldoAFavorPorIdentificar { get; private set; }
    public Guid? MovimientoBancarioId { get; private set; }
    public bool ReppTimbrado { get; private set; }

    public Guid ClienteId { get; private set; }
    public string DepositoRef { get; private set; } = default!;
    public decimal MontoDeposito { get; private set; }
    public string Moneda { get; private set; } = default!;
    public string RemittanceRef { get; private set; } = default!;

    /// <summary>Diferencia depósito − Σ importes: ≤ 0 dentro de tolerancia, o un excedente de redondeo (≤ $0.99, M-326).</summary>
    public decimal AjusteNoFiscal { get; private set; }

    /// <summary>CXC-06: tipo del ajuste (cuenta de la póliza); null si no hay ajuste.</summary>
    public TipoAjusteCobro? TipoAjuste { get; private set; }

    /// <summary>CXC-06: motivo documentado del ajuste; obligatorio cuando hay ajuste.</summary>
    public string? MotivoAjuste { get; private set; }

    /// <summary>CXC-06: quién autorizó el ajuste (nunca quien propuso) y cuándo.</summary>
    public Guid? AjusteAutorizadoPor { get; private set; }
    public DateTimeOffset? AjusteAutorizadoEn { get; private set; }

    /// <summary>
    /// M-240: reversa de un ajuste ya autorizado y contabilizado. Regresa el faltante a la(s) factura(s) y genera la póliza contraria
    /// (ligada a la del cobro). Quién, cuándo, por qué y qué póliza la registró.
    /// </summary>
    public Guid? AjusteRevertidoPor { get; private set; }
    public DateTimeOffset? AjusteRevertidoEn { get; private set; }
    public string? MotivoReversaAjuste { get; private set; }
    public Guid? PolizaReversaAjusteId { get; private set; }

    /// <summary>M-240: el ajuste ya se revirtió.</summary>
    public bool AjusteRevertido => AjusteRevertidoEn is not null;

    /// <summary>CXC-06: REP timbrado de la propuesta (correlaciona el movimiento de cartera con la póliza del cobro).</summary>
    public Guid? ReciboPagoId { get; private set; }

    /// <summary>M-326 parte 2: el redondeo lo autoriza o rechaza quien tenga el permiso de Gerente de Área, no cualquier autorizador de ajustes.</summary>
    public bool RequiereGerenteDeArea => TipoAjuste == TipoAjusteCobro.Redondeo;

    /// <summary>CXC-06: el ajuste espera autorización; mientras tanto la propuesta no se envía a Tesorería.</summary>
    public bool AjustePorAutorizar => AjusteNoFiscal != 0 && AjusteAutorizadoEn is null && Estado == EstadoPropuestaAplicacion.Propuesta;

    public EstadoPropuestaAplicacion Estado { get; private set; }
    public string? MotivoRechazo { get; private set; }
    public Guid? ResueltaPor { get; private set; }
    public DateTimeOffset? ResueltaEn { get; private set; }

    private readonly List<PropuestaAplicacionFactura> _facturas = [];
    public IReadOnlyCollection<PropuestaAplicacionFactura> Facturas => _facturas.AsReadOnly();

    private PropuestaAplicacionPago() { }

    public static PropuestaAplicacionPago Crear(
        Guid empresaId,
        Guid clienteId,
        string depositoRef,
        decimal montoDeposito,
        string moneda,
        string remittanceRef,
        IReadOnlyList<(string FacturaUuid, Guid FacturaCarteraId, decimal ImporteAplicado, int? NumParcialidad)> lineas,
        decimal toleranciaNoFiscal, Guid? propuestoPor = null, Guid? movimientoBancarioId = null,
        TipoAjusteCobro? tipoAjuste = null, string? motivoAjuste = null, DateTimeOffset? ahora = null)
    {
        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("PAP_CLIENTE_VACIO", "El cliente es obligatorio.");
        if (string.IsNullOrWhiteSpace(depositoRef))
            throw new BusinessRuleException("PAP_DEPOSITO_VACIO", "La referencia del depósito es obligatoria.");
        if (montoDeposito <= 0)
            throw new BusinessRuleException("PAP_MONTO_INVALIDO", "El monto del depósito debe ser > 0.");
        // Regla 2.1 del levantamiento: sin remittance no hay propuesta.
        if (string.IsNullOrWhiteSpace(remittanceRef))
            throw new BusinessRuleException("PAP_SIN_REMITTANCE",
                "Sin remittance del cliente no se puede proponer la aplicación.");
        if (lineas.Count == 0)
            throw new BusinessRuleException("PAP_SIN_FACTURAS", "La propuesta debe aplicar al menos una factura.");
        if (lineas.Any(l => l.ImporteAplicado <= 0))
            throw new BusinessRuleException("PAP_IMPORTE_INVALIDO", "Cada importe aplicado debe ser > 0.");
        if (lineas.GroupBy(l => l.FacturaUuid).Any(g => g.Count() > 1))
            throw new BusinessRuleException("PAP_FACTURA_DUPLICADA", "Una factura no puede repetirse en la propuesta.");

        var suma = lineas.Sum(l => l.ImporteAplicado);
        var ajuste = montoDeposito - suma;

        // M-326: el faltante de hasta $0.99 sin tipo documentado es redondeo; elegirlo a mano no se permite (lo asigna el sistema).
        if (tipoAjuste == TipoAjusteCobro.Redondeo)
            throw new BusinessRuleException("PAP_AJUSTE_TIPO_INVALIDO", "El redondeo lo asigna el sistema a los faltantes de hasta $0.99; no se elige como tipo de ajuste.");
        var esRedondeo = ajuste < 0 && Math.Abs(ajuste) <= ToleranciaRedondeo && tipoAjuste is null;
        // M-326 parte 2 (a): un excedente de hasta $0.99 (el cliente depositó centavos de más) también es redondeo: va a REDONDEO como
        // abono, no a saldo a favor. Un excedente mayor sigue como saldo a favor por identificar.
        var esExcedenteRedondeo = ajuste > 0 && ajuste <= ToleranciaRedondeo && tipoAjuste is null;
        var redondea = esRedondeo || esExcedenteRedondeo;

        if (!esRedondeo && ajuste < 0 && Math.Abs(ajuste) >= toleranciaNoFiscal)
            throw new BusinessRuleException("PAP_TOLERANCIA_EXCEDIDA",
                $"La diferencia ({ajuste}) excede la tolerancia no fiscal ({toleranciaNoFiscal} {moneda}).");

        // CXC-06: un faltante dentro de la tolerancia no se absorbe solo; se documenta (tipo = cuenta, y motivo) y se autoriza.
        var conAjuste = ajuste < 0;
        if (conAjuste && !esRedondeo && tipoAjuste is null)
            throw new BusinessRuleException("PAP_AJUSTE_SIN_TIPO",
                $"El depósito es {Math.Abs(ajuste):N2} {moneda} menor que lo aplicado: indica si es comisión bancaria o diferencia menor.");
        if (conAjuste && !esRedondeo && string.IsNullOrWhiteSpace(motivoAjuste))
            throw new BusinessRuleException("PAP_AJUSTE_SIN_MOTIVO",
                "El motivo del ajuste es obligatorio: queda documentado en la póliza del cobro.");
        if (tipoAjuste is { } t && !Enum.IsDefined(t))
            throw new BusinessRuleException("PAP_AJUSTE_TIPO_INVALIDO", "El tipo de ajuste no es válido.");

        var propuesta = new PropuestaAplicacionPago
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            ClienteId = clienteId,
            DepositoRef = depositoRef.Trim(),
            MontoDeposito = montoDeposito,
            Moneda = moneda,
            RemittanceRef = remittanceRef.Trim(),
            AjusteNoFiscal = esExcedenteRedondeo ? ajuste : Math.Min(0, ajuste),
            SaldoAFavorPorIdentificar = esExcedenteRedondeo ? 0 : Math.Max(0, ajuste),
            PropuestoPor = propuestoPor,
            // C7.4: depósito sin identificar que CxC identificó (Tesorería lo
            // preselecciona); al confirmar, Tesorería informa el movimiento real.
            MovimientoBancarioId = movimientoBancarioId,
            TipoAjuste = redondea ? TipoAjusteCobro.Redondeo : conAjuste ? tipoAjuste : null,
            MotivoAjuste = redondea ? $"Redondeo de hasta ${ToleranciaRedondeo:0.00}" : conAjuste ? motivoAjuste!.Trim() : null,
            // M-326 parte 2 (c): el redondeo también espera la autorización del Gerente de Área (otra persona, no quien propone).
            Estado = EstadoPropuestaAplicacion.Propuesta,
        };

        foreach (var l in lineas)
        {
            propuesta._facturas.Add(new PropuestaAplicacionFactura(
                Guid.CreateVersion7(), empresaId, propuesta.Id,
                l.FacturaCarteraId, l.FacturaUuid, l.ImporteAplicado, l.NumParcialidad));
        }

        return propuesta;
    }

    /// <summary>Confirmación recibida de Tesorería — la cartera espera el REP.</summary>
    public void Confirmar(Guid usuarioId, DateTimeOffset ahora, Guid? movimientoBancarioId = null)
    {
        AsegurarPropuesta();
        if (PropuestoPor is null || PropuestoPor == Guid.Empty)
            throw new BusinessRuleException("PAP_PROPONENTE_NO_IDENTIFICADO", "La propuesta no identifica quién la creó. Recházala y registra una nueva.");
        if (PropuestoPor == usuarioId)
            throw new BusinessRuleException("DEP_MISMO_USUARIO", "Quien propone el cobro no puede confirmarlo. Solicita la confirmación a otra persona de Tesorería.");
        MovimientoBancarioId = movimientoBancarioId ?? MovimientoBancarioId;
        Estado = EstadoPropuestaAplicacion.Confirmada;
        ResueltaPor = usuarioId;
        ResueltaEn = ahora;
    }

    public void Rechazar(Guid usuarioId, string motivo, DateTimeOffset ahora)
    {
        AsegurarPropuesta();
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("PAP_MOTIVO_VACIO", "El motivo de rechazo es obligatorio.");

        Estado = EstadoPropuestaAplicacion.Rechazada;
        MotivoRechazo = motivo.Trim();
        ResueltaPor = usuarioId;
        ResueltaEn = ahora;
    }

    /// <summary>
    /// CXC-06: autoriza el ajuste documentado. Separación de funciones: quien propuso no autoriza su propio ajuste.
    /// </summary>
    /// <param name="tieneSoporte">M-240: hay al menos un adjunto vigente (estado de cuenta o aviso del banco); obligatorio para la comisión.</param>
    public void AutorizarAjuste(Guid usuarioId, DateTimeOffset ahora, bool tieneSoporte = false)
    {
        AsegurarPropuesta();
        if (AjusteNoFiscal == 0)
            throw new BusinessRuleException("PAP_SIN_AJUSTE", "La propuesta no tiene ajuste que autorizar.");
        if (AjusteAutorizadoEn is not null)
            throw new BusinessRuleException("PAP_AJUSTE_YA_AUTORIZADO", "El ajuste ya fue autorizado.");
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("PAP_AUTORIZADOR_NO_IDENTIFICADO", "No se identificó a quien autoriza.");
        if (PropuestoPor == usuarioId)
            throw new BusinessRuleException("PAP_AJUSTE_MISMO_USUARIO",
                "Quien propone el cobro no puede autorizar su propio ajuste. Solicita la autorización a otra persona.");
        if (TipoAjuste == TipoAjusteCobro.ComisionBancaria && !tieneSoporte)
            throw new BusinessRuleException("PAP_AJUSTE_SIN_SOPORTE",
                "La comisión bancaria necesita su soporte: adjunta el estado de cuenta o el aviso del banco antes de autorizarla.");
        AjusteAutorizadoPor = usuarioId;
        AjusteAutorizadoEn = ahora;
    }

    /// <summary>
    /// M-240: revierte un ajuste autorizado de un cobro ya confirmado y con REP timbrado (es decir, ya aplicado en cartera y
    /// contabilizado). Motivo obligatorio; quien propuso el cobro no revierte su propio ajuste (misma separación que al autorizar).
    /// La cartera y la póliza contraria las hace el caso de uso; aquí solo se valida y se registra.
    /// </summary>
    public void RevertirAjuste(Guid usuarioId, string motivo, DateTimeOffset ahora)
    {
        if (AjusteNoFiscal >= 0 || AjusteAutorizadoEn is null)
            // M-326 parte 2: el excedente de redondeo (ajuste positivo) no se revierte aquí; solo el faltante.
            throw new BusinessRuleException("PAP_SIN_AJUSTE_AUTORIZADO", "La propuesta no tiene un ajuste autorizado que revertir.");
        if (AjusteRevertidoEn is not null)
            throw new BusinessRuleException("PAP_AJUSTE_YA_REVERTIDO", "El ajuste ya fue revertido.");
        if (Estado != EstadoPropuestaAplicacion.Confirmada || !ReppTimbrado || ReciboPagoId is null)
            throw new BusinessRuleException("PAP_AJUSTE_NO_APLICADO",
                "Solo se revierte el ajuste de un cobro confirmado por Tesorería y con su REP timbrado. Si aún no se confirma, rechaza la propuesta.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("PAP_MOTIVO_REVERSA_VACIO", "El motivo de la reversa es obligatorio.");
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("PAP_AUTORIZADOR_NO_IDENTIFICADO", "No se identificó a quien revierte.");
        if (PropuestoPor == usuarioId)
            throw new BusinessRuleException("PAP_REVERSA_MISMO_USUARIO",
                "Quien propuso el cobro no puede revertir su ajuste. Solicita la reversa a otra persona.");
        AjusteRevertidoPor = usuarioId;
        AjusteRevertidoEn = ahora;
        MotivoReversaAjuste = motivo.Trim();
    }

    /// <summary>M-240: póliza contraria registrada por Contabilidad para la reversa.</summary>
    public void RegistrarPolizaReversaAjuste(Guid polizaId)
    {
        if (AjusteRevertidoEn is null)
            throw new BusinessRuleException("PAP_AJUSTE_NO_REVERTIDO", "El ajuste no está revertido.");
        PolizaReversaAjusteId = polizaId;
    }

    public void MarcarReppTimbrado(Guid? reciboPagoId = null)
    {
        ReppTimbrado = true;
        ReciboPagoId ??= reciboPagoId;
    }

    private void AsegurarPropuesta()
    {
        if (Estado != EstadoPropuestaAplicacion.Propuesta)
            throw new BusinessRuleException("PAP_YA_RESUELTA",
                $"Solo una propuesta pendiente puede resolverse (estado actual: {Estado}).");
    }
}

/// <summary>Detalle por factura de la propuesta (unión §3.1).</summary>
public sealed class PropuestaAplicacionFactura : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid PropuestaId { get; private set; }

    /// <summary>Factura en la proyección de cartera (correlación local).</summary>
    public Guid FacturaCarteraId { get; private set; }

    /// <summary>UUID fiscal — la referencia que viaja en el remittance.</summary>
    public string FacturaUuid { get; private set; } = default!;

    public decimal ImporteAplicado { get; private set; }
    public int? NumParcialidad { get; private set; }

    private PropuestaAplicacionFactura() { }

    internal PropuestaAplicacionFactura(
        Guid id, Guid empresaId, Guid propuestaId, Guid facturaCarteraId,
        string facturaUuid, decimal importeAplicado, int? numParcialidad) : base(id)
    {
        EmpresaId = empresaId;
        PropuestaId = propuestaId;
        FacturaCarteraId = facturaCarteraId;
        FacturaUuid = facturaUuid;
        ImporteAplicado = importeAplicado;
        NumParcialidad = numParcialidad;
    }
}
