using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.Alertas;

/// <summary>Tipos de alerta de cartera (§3.1 del 00-levantamiento).</summary>
public enum TipoAlertaCartera : short
{
    /// <summary>Factura de cliente asegurado SOLUNION vencida ≥ N días (default 90) — límite para reportar siniestro.</summary>
    Solunion90d = 1,

    /// <summary>Saldo total del cliente excede el límite de su línea (saldo excedido §2.2).</summary>
    ExcesoCredito = 2,

    /// <summary>Vencimientos dispararon el auto-bloqueo de la línea de crédito.</summary>
    AutoBloqueoVencimiento = 3,
}

/// <summary>
/// Alerta de cartera generada por el worker de evaluación diaria
/// (§3.1 del 00-levantamiento, CXC-PR8). Visible en la bandeja
/// <c>/cxc/alertas</c>; las notificaciones push/correo quedan en
/// PLATFORM-TODO(&lt;Notificaciones&gt;) hasta que exista el motor
/// transversal (01-diseño §13).
/// </summary>
public sealed class AlertaCartera : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }

    public Guid ClienteId { get; private set; }
    public TipoAlertaCartera Tipo { get; private set; }
    public string Moneda { get; private set; } = default!;
    public string Detalle { get; private set; } = default!;
    public DateTimeOffset DisparadaEn { get; private set; }

    public bool Atendida { get; private set; }
    public Guid? AtendidaPor { get; private set; }
    public DateTimeOffset? AtendidaEn { get; private set; }

    private AlertaCartera() { }

    public AlertaCartera(
        Guid empresaId,
        Guid clienteId,
        TipoAlertaCartera tipo,
        string moneda,
        string detalle,
        DateTimeOffset disparadaEn) : base(Guid.CreateVersion7())
    {
        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("AL_CLIENTE_VACIO", "El cliente es obligatorio.");
        if (string.IsNullOrWhiteSpace(detalle))
            throw new BusinessRuleException("AL_DETALLE_VACIO", "El detalle de la alerta es obligatorio.");

        EmpresaId = empresaId;
        ClienteId = clienteId;
        Tipo = tipo;
        Moneda = moneda;
        Detalle = detalle.Trim();
        DisparadaEn = disparadaEn;
        Atendida = false;
    }

    public void Atender(Guid usuarioId, DateTimeOffset ahora)
    {
        if (Atendida)
            throw new BusinessRuleException("AL_YA_ATENDIDA", "La alerta ya fue atendida.");
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("AL_USUARIO_VACIO", "El usuario que atiende es obligatorio.");

        Atendida = true;
        AtendidaPor = usuarioId;
        AtendidaEn = ahora;
    }
}
