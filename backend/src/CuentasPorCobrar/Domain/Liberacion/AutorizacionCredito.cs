using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.Liberacion;

/// <summary>Estados de una <see cref="AutorizacionCredito"/>.</summary>
public enum EstadoAutorizacionCredito : short
{
    /// <summary>Vigente; el beneficiario puede consumirla al decidir.</summary>
    Autorizada = 1,

    /// <summary>Consumida por una decisión de liberación (un solo uso).</summary>
    Usada = 2,

    /// <summary>Cancelada por el supervisor antes de usarse.</summary>
    Cancelada = 3,

    /// <summary>
    /// M-239: la excepción pide dos firmas por su monto y falta la de Dirección; mientras tanto
    /// no se puede consumir.
    /// </summary>
    PendienteSegundaFirma = 4,
}

/// <summary>M-239: condición excepcional por la que se pide la autorización (la solicitud la registra).</summary>
public enum CondicionExcepcionCredito : short
{
    /// <summary>El crédito cubre 50–99 % del pedido; la excepción cubre el exceso.</summary>
    CoberturaParcial = 1,

    /// <summary>Serie que nunca libera sola; la excepción cubre el importe del pedido.</summary>
    SerieNuncaLibera = 2,

    /// <summary>Cliente bloqueado por vencimiento; la excepción cubre el importe del pedido.</summary>
    BloqueoPorVencimiento = 3,
}

/// <summary>
/// Override de crédito consumible (§3.1 del 00-levantamiento, CXC-PR4) —
/// calco de <c>AutorizacionAperturaCaja</c> (`[Decisión 12-1]`, tercera
/// réplica del patrón; candidato a ADR transversal). Un gerente
/// (<c>cuentas_por_cobrar.liberacion.override</c>) autoriza que un
/// usuario específico libere un pedido (E10: uno solo) pese al crédito, con
/// vigencia (por omisión 24 h, máximo configurable) y un solo uso. Nunca autoconsumo.
/// </summary>
public sealed class AutorizacionCredito : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    /// <summary>Gerente que autorizó (rastro de auditoría).</summary>
    public Guid SupervisorUsuarioId { get; private set; }

    /// <summary>Beneficiario — el único que puede consumirla.</summary>
    public Guid BeneficiarioUsuarioId { get; private set; }

    public string Motivo { get; private set; } = string.Empty;

    /// <summary>
    /// Folio del pedido al que aplica el override (C7.3 · E10: por pedido;
    /// la columna conserva su nombre histórico «cliente o pedido»).
    /// </summary>
    public string ClienteOPedidoRef { get; private set; } = string.Empty;

    public DateTimeOffset FechaAutorizacion { get; private set; }
    public DateTimeOffset VigenteHasta { get; private set; }

    public EstadoAutorizacionCredito Estado { get; private set; }

    /// <summary>Decisión que la consumió; null hasta usarse.</summary>
    public Guid? DecisionLiberacionId { get; private set; }

    private AutorizacionCredito() { }

    private AutorizacionCredito(
        Guid id, Guid empresaId, Guid supervisorUsuarioId, Guid beneficiarioUsuarioId,
        string motivo, string clienteOPedidoRef,
        DateTimeOffset fechaAutorizacion, DateTimeOffset vigenteHasta) : base(id)
    {
        EmpresaId = empresaId;
        SupervisorUsuarioId = supervisorUsuarioId;
        BeneficiarioUsuarioId = beneficiarioUsuarioId;
        Motivo = motivo;
        ClienteOPedidoRef = clienteOPedidoRef;
        FechaAutorizacion = fechaAutorizacion;
        VigenteHasta = vigenteHasta;
        Estado = EstadoAutorizacionCredito.Autorizada;
    }

    /// <summary>
    /// M-085 (Crédito y Cobranza, 9-nov): la otorgó alguien con el permiso de Dirección. Crédito y Cobranza autoriza hasta
    /// <see cref="ExcesoCredito.TopeCreditoYCobranza"/> de exceso; arriba de eso solo vale una autorización de Dirección.
    /// </summary>
    public bool AutorizadaPorDireccion { get; private set; }

    /// <summary>M-239: condición excepcional que registró la solicitud; null en autorizaciones anteriores.</summary>
    public CondicionExcepcionCredito? Condicion { get; private set; }

    /// <summary>
    /// M-239: monto que cubre la excepción (exceso sobre el crédito en cobertura parcial; importe del pedido en
    /// serie que nunca libera o bloqueo por vencimiento). Limita lo que se libera. Null = sin tope (anteriores a M-239).
    /// </summary>
    public decimal? MontoAutorizado { get; private set; }

    /// <summary>Moneda de <see cref="MontoAutorizado"/>.</summary>
    public string Moneda { get; private set; } = "MXN";

    /// <summary>M-239: firmas que pide la excepción por su monto (1 o 2); la segunda es de Dirección.</summary>
    public short FirmasRequeridas { get; private set; } = 1;

    /// <summary>M-239: Dirección que dio la segunda firma.</summary>
    public Guid? SegundaFirmaPorId { get; private set; }
    public DateTimeOffset? SegundaFirmaEn { get; private set; }

    public static AutorizacionCredito Crear(
        Guid empresaId,
        Guid supervisorUsuarioId,
        Guid beneficiarioUsuarioId,
        string motivo,
        string clienteOPedidoRef,
        DateTimeOffset ahora,
        TimeSpan vigencia,
        bool autorizadaPorDireccion)
    {
        var a = Crear(empresaId, supervisorUsuarioId, beneficiarioUsuarioId, motivo, clienteOPedidoRef, ahora, vigencia);
        a.AutorizadaPorDireccion = autorizadaPorDireccion;
        return a;
    }

    /// <summary>
    /// M-239: solicitud de excepción con monto y condición. La vigencia máxima sale de configuración. Si pide dos
    /// firmas y no la otorga Dirección, queda <see cref="EstadoAutorizacionCredito.PendienteSegundaFirma"/> hasta
    /// <see cref="Firmar"/>; si la otorga Dirección, ocupa ambos niveles y queda autorizada.
    /// </summary>
    public static AutorizacionCredito CrearExcepcional(
        Guid empresaId,
        Guid supervisorUsuarioId,
        Guid beneficiarioUsuarioId,
        string motivo,
        string clienteOPedidoRef,
        DateTimeOffset ahora,
        TimeSpan vigencia,
        TimeSpan vigenciaMaxima,
        CondicionExcepcionCredito condicion,
        decimal montoAutorizado,
        string moneda,
        short firmasRequeridas,
        bool autorizadaPorDireccion)
    {
        if (!Enum.IsDefined(condicion))
            throw new BusinessRuleException("AC_CONDICION_INVALIDA", "La condición de la excepción no es válida.");
        if (montoAutorizado <= 0)
            throw new BusinessRuleException("AC_MONTO_INVALIDO", "El monto de la excepción debe ser mayor a cero.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Trim().Length != 3)
            throw new BusinessRuleException("AC_MONEDA_INVALIDA", "La moneda debe ser un código de tres letras.");
        if (firmasRequeridas is < 1 or > 2)
            throw new BusinessRuleException("AC_FIRMAS_INVALIDAS", "Una excepción pide una o dos firmas.");

        var a = Crear(empresaId, supervisorUsuarioId, beneficiarioUsuarioId, motivo, clienteOPedidoRef, ahora, vigencia, vigenciaMaxima);
        a.Condicion = condicion;
        a.MontoAutorizado = Math.Round(montoAutorizado, 2, MidpointRounding.AwayFromZero);
        a.Moneda = moneda.Trim().ToUpperInvariant();
        a.FirmasRequeridas = firmasRequeridas;
        a.AutorizadaPorDireccion = autorizadaPorDireccion;
        if (firmasRequeridas == 2 && !autorizadaPorDireccion)
            a.Estado = EstadoAutorizacionCredito.PendienteSegundaFirma;
        return a;
    }

    /// <summary>
    /// M-239: la segunda firma (Dirección) deja autorizada la excepción pendiente. Debe ser otra persona que quien la
    /// solicitó y que el beneficiario, y darse dentro de la vigencia de la solicitud (supuesto a validar).
    /// </summary>
    public void Firmar(Guid usuarioId, DateTimeOffset ahora)
    {
        if (Estado != EstadoAutorizacionCredito.PendienteSegundaFirma)
            throw new BusinessRuleException("AC_NO_PENDIENTE_FIRMA",
                $"La autorización no espera segunda firma (estado actual: {Estado}).");
        if (usuarioId == SupervisorUsuarioId)
            throw new BusinessRuleException("AC_FIRMA_MISMA_PERSONA",
                "Quien solicitó la excepción no puede dar también la segunda firma; firma otra persona.");
        if (usuarioId == BeneficiarioUsuarioId)
            throw new BusinessRuleException("AC_FIRMA_BENEFICIARIO",
                "El beneficiario no puede dar la segunda firma de su propia excepción; firma otra persona.");
        if (ahora > VigenteHasta)
            throw new BusinessRuleException("AC_VENCIDA",
                "La solicitud de autorización ya venció; solicita una nueva.");

        SegundaFirmaPorId = usuarioId;
        SegundaFirmaEn = ahora;
        AutorizadaPorDireccion = true;
        Estado = EstadoAutorizacionCredito.Autorizada;
    }

    /// <summary>M-239: una excepción que espera la segunda firma de Dirección no se puede usar.</summary>
    public void ExigirFirmaCompleta()
    {
        if (Estado == EstadoAutorizacionCredito.PendienteSegundaFirma)
            throw new BusinessRuleException("AC_PENDIENTE_SEGUNDA_FIRMA",
                "La autorización espera la segunda firma de Dirección; todavía no se puede usar.");
    }

    /// <summary>M-239: la condición real del pedido debe ser la que registró la solicitud (si la registró).</summary>
    public void ExigirCondicion(CondicionExcepcionCredito real)
    {
        if (Condicion is not null && Condicion != real)
            throw new BusinessRuleException("AC_CONDICION_DISTINTA",
                $"La autorización se pidió por «{Condicion}» y este pedido está en «{real}».");
    }

    /// <summary>M-239: el monto autorizado limita lo que se libera (misma moneda y sin pasar del monto).</summary>
    public void ExigirMonto(decimal montoLiberado, string moneda)
    {
        if (MontoAutorizado is not decimal tope) return;
        if (!string.Equals(Moneda, moneda?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("AC_MONEDA_DISTINTA",
                $"La autorización es en {Moneda} y el pedido es en {moneda}.");
        if (montoLiberado > tope)
            throw new BusinessRuleException("AC_MONTO_EXCEDE",
                $"La autorización cubre hasta {tope:N2} {Moneda} y esta liberación requiere {montoLiberado:N2}.");
    }

    public static AutorizacionCredito Crear(
        Guid empresaId,
        Guid supervisorUsuarioId,
        Guid beneficiarioUsuarioId,
        string motivo,
        string clienteOPedidoRef,
        DateTimeOffset ahora,
        TimeSpan vigencia,
        TimeSpan? vigenciaMaxima = null)
    {
        var maxima = vigenciaMaxima ?? TimeSpan.FromHours(24);
        if (supervisorUsuarioId == Guid.Empty)
            throw new BusinessRuleException("AC_SUPERVISOR_INVALIDO", "El supervisor es obligatorio.");
        if (beneficiarioUsuarioId == Guid.Empty)
            throw new BusinessRuleException("AC_BENEFICIARIO_INVALIDO", "El beneficiario es obligatorio.");
        if (beneficiarioUsuarioId == supervisorUsuarioId)
            throw new BusinessRuleException("AC_AUTOCONSUMO",
                "El supervisor no puede autorizarse a sí mismo; la decisión debe tomarla otro usuario.");
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > 254)
            throw new BusinessRuleException("AC_MOTIVO_INVALIDO", "El motivo es obligatorio (máx. 254 caracteres).");
        if (string.IsNullOrWhiteSpace(clienteOPedidoRef))
            throw new BusinessRuleException("AC_REF_VACIA", "El cliente o pedido del override es obligatorio.");
        if (vigencia <= TimeSpan.Zero || vigencia > maxima)
            throw new BusinessRuleException("AC_VIGENCIA_INVALIDA",
                $"La vigencia debe ser positiva y de máximo {maxima.TotalHours:0.##} horas.");

        return new AutorizacionCredito(
            Guid.CreateVersion7(), empresaId, supervisorUsuarioId, beneficiarioUsuarioId,
            motivo.Trim(), clienteOPedidoRef.Trim(), ahora, ahora.Add(vigencia));
    }

    /// <summary>
    /// Consume la autorización al emitir la decisión (un solo uso).
    /// C7.3 · E10 (Eliam, 26-oct): la autorización es POR PEDIDO —
    /// <see cref="ClienteOPedidoRef"/> debe ser exactamente el folio del
    /// pedido que se decide (tras <c>Trim</c>, comparación ordinal). Ya no
    /// se acepta la referencia por cliente (id, RFC o razón social): una
    /// autorización otorgada para un pedido no libera otro.
    /// </summary>
    public void Consumir(
        Guid decisionLiberacionId,
        Guid beneficiarioUsuarioId,
        DateTimeOffset ahora,
        string pedidoRef)
    {
        ExigirFirmaCompleta();
        if (Estado != EstadoAutorizacionCredito.Autorizada)
            throw new BusinessRuleException(
                "AC_NO_DISPONIBLE",
                $"La autorización no está disponible (estado actual: {Estado}).");
        if (ahora > VigenteHasta)
            throw new BusinessRuleException("AC_VENCIDA",
                "La autorización de crédito ya venció; solicite una nueva al gerente.");
        if (beneficiarioUsuarioId != BeneficiarioUsuarioId)
            throw new BusinessRuleException("AC_BENEFICIARIO_DISTINTO",
                "La autorización es personal: solo el beneficiario puede consumirla.");
        if (!AplicaAPedido(pedidoRef))
            throw new BusinessRuleException("AC_REFERENCIA_DISTINTA",
                $"La autorización se otorgó para el pedido «{ClienteOPedidoRef}» y no aplica a este pedido.");

        Estado = EstadoAutorizacionCredito.Usada;
        DecisionLiberacionId = decisionLiberacionId;
    }

    /// <summary>¿La autorización se otorgó para este folio de pedido (E10)?</summary>
    public bool AplicaAPedido(string? pedidoRef) =>
        !string.IsNullOrWhiteSpace(pedidoRef)
        && string.Equals(pedidoRef.Trim(), ClienteOPedidoRef.Trim(), StringComparison.Ordinal);

    /// <summary>Cancela una autorización aún no usada.</summary>
    public void Cancelar()
    {
        if (Estado is not (EstadoAutorizacionCredito.Autorizada or EstadoAutorizacionCredito.PendienteSegundaFirma))
            throw new BusinessRuleException(
                "AC_NO_DISPONIBLE",
                $"Solo una autorización vigente puede cancelarse (estado actual: {Estado}).");

        Estado = EstadoAutorizacionCredito.Cancelada;
    }
}
