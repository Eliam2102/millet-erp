using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Cajas;

/// <summary>Estados de una <see cref="AutorizacionAperturaCaja"/>.</summary>
public enum EstadoAutorizacionAperturaCaja : short
{
    /// <summary>Vigente; el cajero beneficiario puede consumirla al abrir.</summary>
    Autorizada = 1,

    /// <summary>Consumida por una apertura de sesión (un solo uso).</summary>
    Usada = 2,

    /// <summary>Cancelada por el supervisor antes de usarse.</summary>
    Cancelada = 3,
}

/// <summary>
/// Autorización previa consumible para abrir una caja ajena
/// (`[Decisión 12-1]`, 12-cajas.md §5.3) — calco de
/// <c>AutorizacionVentaActivo</c>. Un supervisor
/// (<c>facturacion.caja.supervisar</c>) autoriza que un cajero específico
/// abra una caja específica, con vigencia corta (default 30 min,
/// configurable) y un solo uso. Nunca contraseña compartida: el sistema es
/// passwordless vía Entra ID y no existe re-autenticación inline.
/// </summary>
public sealed class AutorizacionAperturaCaja : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid CajaId { get; private set; }

    /// <summary>Cajero beneficiario — el único que puede consumirla.</summary>
    public Guid CajeroUsuarioId { get; private set; }

    /// <summary>Supervisor que autorizó (rastro de auditoría, §5.3).</summary>
    public Guid SupervisorUsuarioId { get; private set; }

    public string Motivo { get; private set; } = string.Empty;
    public DateTimeOffset FechaAutorizacion { get; private set; }
    public DateTimeOffset VigenteHasta { get; private set; }

    public EstadoAutorizacionAperturaCaja Estado { get; private set; }

    /// <summary>Sesión que la consumió; null hasta usarse.</summary>
    public Guid? CajaSesionId { get; private set; }

    private AutorizacionAperturaCaja() { }

    private AutorizacionAperturaCaja(
        Guid id, Guid empresaId, Guid cajaId, Guid cajeroUsuarioId, Guid supervisorUsuarioId,
        string motivo, DateTimeOffset fechaAutorizacion, DateTimeOffset vigenteHasta) : base(id)
    {
        EmpresaId = empresaId;
        CajaId = cajaId;
        CajeroUsuarioId = cajeroUsuarioId;
        SupervisorUsuarioId = supervisorUsuarioId;
        Motivo = motivo;
        FechaAutorizacion = fechaAutorizacion;
        VigenteHasta = vigenteHasta;
        Estado = EstadoAutorizacionAperturaCaja.Autorizada;
    }

    public static AutorizacionAperturaCaja Crear(
        Guid empresaId,
        Guid cajaId,
        Guid cajeroUsuarioId,
        Guid supervisorUsuarioId,
        string motivo,
        DateTimeOffset ahora,
        TimeSpan vigencia)
    {
        if (cajaId == Guid.Empty)
            throw new BusinessRuleException("AUTORIZACION_CAJA_INVALIDA", "La caja es obligatoria.");
        if (cajeroUsuarioId == Guid.Empty)
            throw new BusinessRuleException("AUTORIZACION_CAJERO_INVALIDO", "El cajero beneficiario es obligatorio.");
        if (supervisorUsuarioId == Guid.Empty)
            throw new BusinessRuleException("AUTORIZACION_SUPERVISOR_INVALIDO", "El supervisor es obligatorio.");
        if (cajeroUsuarioId == supervisorUsuarioId)
            throw new BusinessRuleException("AUTORIZACION_AUTOCONSUMO", "El supervisor no puede autorizarse a sí mismo; si opera la caja, relaciónese a ella.");
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > 254)
            throw new BusinessRuleException("AUTORIZACION_MOTIVO_INVALIDO", "El motivo es obligatorio (máx. 254 caracteres).");
        if (vigencia <= TimeSpan.Zero || vigencia > TimeSpan.FromHours(24))
            throw new BusinessRuleException("AUTORIZACION_VIGENCIA_INVALIDA", "La vigencia debe ser positiva y de máximo 24 horas.");

        return new AutorizacionAperturaCaja(
            Guid.CreateVersion7(), empresaId, cajaId, cajeroUsuarioId, supervisorUsuarioId,
            motivo.Trim(), ahora, ahora.Add(vigencia));
    }

    /// <summary>Consume la autorización al abrir la sesión (un solo uso, §5.3 paso 2).</summary>
    public void Consumir(Guid cajaSesionId, Guid cajeroUsuarioId, Guid cajaId, DateTimeOffset ahora)
    {
        if (Estado != EstadoAutorizacionAperturaCaja.Autorizada)
            throw new BusinessRuleException(
                "AUTORIZACION_NO_DISPONIBLE",
                $"La autorización no está disponible (estado actual: {Estado}).");
        if (ahora > VigenteHasta)
            throw new BusinessRuleException("AUTORIZACION_VENCIDA", "La autorización de apertura ya venció; solicite una nueva al supervisor.");
        if (cajeroUsuarioId != CajeroUsuarioId)
            throw new BusinessRuleException("AUTORIZACION_CAJERO_DISTINTO", "La autorización es personal: solo el cajero beneficiario puede consumirla.");
        if (cajaId != CajaId)
            throw new BusinessRuleException("AUTORIZACION_CAJA_DISTINTA", "La autorización aplica a otra caja.");

        Estado = EstadoAutorizacionAperturaCaja.Usada;
        CajaSesionId = cajaSesionId;
    }

    /// <summary>Cancela una autorización aún no usada.</summary>
    public void Cancelar()
    {
        if (Estado != EstadoAutorizacionAperturaCaja.Autorizada)
            throw new BusinessRuleException(
                "AUTORIZACION_NO_DISPONIBLE",
                $"Solo una autorización vigente puede cancelarse (estado actual: {Estado}).");

        Estado = EstadoAutorizacionAperturaCaja.Cancelada;
    }
}
