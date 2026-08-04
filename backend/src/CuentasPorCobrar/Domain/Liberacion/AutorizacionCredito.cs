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
}

/// <summary>
/// Override de crédito consumible (§3.1 del 00-levantamiento, CXC-PR4) —
/// calco de <c>AutorizacionAperturaCaja</c> (`[Decisión 12-1]`, tercera
/// réplica del patrón; candidato a ADR transversal). Un gerente
/// (<c>cuentas_por_cobrar.liberacion.override</c>) autoriza que un
/// usuario específico libere un pedido/cliente pese al crédito, con
/// vigencia ≤ 24 h y un solo uso. Nunca autoconsumo.
/// </summary>
public sealed class AutorizacionCredito : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    /// <summary>Gerente que autorizó (rastro de auditoría).</summary>
    public Guid SupervisorUsuarioId { get; private set; }

    /// <summary>Beneficiario — el único que puede consumirla.</summary>
    public Guid BeneficiarioUsuarioId { get; private set; }

    public string Motivo { get; private set; } = string.Empty;

    /// <summary>Cliente o folio de pedido al que aplica el override.</summary>
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

    public static AutorizacionCredito Crear(
        Guid empresaId,
        Guid supervisorUsuarioId,
        Guid beneficiarioUsuarioId,
        string motivo,
        string clienteOPedidoRef,
        DateTimeOffset ahora,
        TimeSpan vigencia)
    {
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
        if (vigencia <= TimeSpan.Zero || vigencia > TimeSpan.FromHours(24))
            throw new BusinessRuleException("AC_VIGENCIA_INVALIDA", "La vigencia debe ser positiva y de máximo 24 horas.");

        return new AutorizacionCredito(
            Guid.CreateVersion7(), empresaId, supervisorUsuarioId, beneficiarioUsuarioId,
            motivo.Trim(), clienteOPedidoRef.Trim(), ahora, ahora.Add(vigencia));
    }

    /// <summary>Consume la autorización al emitir la decisión (un solo uso).</summary>
    public void Consumir(Guid decisionLiberacionId, Guid beneficiarioUsuarioId, DateTimeOffset ahora)
    {
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

        Estado = EstadoAutorizacionCredito.Usada;
        DecisionLiberacionId = decisionLiberacionId;
    }

    /// <summary>Cancela una autorización aún no usada.</summary>
    public void Cancelar()
    {
        if (Estado != EstadoAutorizacionCredito.Autorizada)
            throw new BusinessRuleException(
                "AC_NO_DISPONIBLE",
                $"Solo una autorización vigente puede cancelarse (estado actual: {Estado}).");

        Estado = EstadoAutorizacionCredito.Cancelada;
    }
}
