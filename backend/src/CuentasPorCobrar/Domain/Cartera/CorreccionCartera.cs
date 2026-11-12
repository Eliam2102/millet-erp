using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.Cartera;

public enum TipoCorreccionCartera : short
{
    /// <summary>Cambio del plazo de una factura (recalcula vencimiento y antigüedad).</summary>
    Plazo = 1,

    /// <summary>Reversa de un movimiento de cartera mal aplicado (pide autorización de otra persona).</summary>
    ReversaMovimiento = 2,
}

public enum EstadoCorreccionCartera : short
{
    /// <summary>Solicitada: espera la decisión de quien autoriza.</summary>
    Pendiente = 1,

    /// <summary>Aplicada en cartera.</summary>
    Aplicada = 2,

    /// <summary>Rechazada por quien autoriza; la cartera no cambió.</summary>
    Rechazada = 3,
}

/// <summary>
/// M-237 (CXC-CARTERA/corregir): rastro append-only de una corrección hecha desde el estado de cuenta, con quién la pidió, quién la
/// autorizó, por qué y el antes/después. El plazo se aplica al momento (queda registrada y aplicada); la reversa de un movimiento
/// se solicita y la autoriza otra persona (misma separación de funciones que el ajuste de cobro).
/// </summary>
public sealed class CorreccionCartera : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public Guid FacturaCarteraId { get; private set; }
    public TipoCorreccionCartera Tipo { get; private set; }
    public EstadoCorreccionCartera Estado { get; private set; }

    public string Motivo { get; private set; } = default!;

    public Guid SolicitadaPorId { get; private set; }
    public string? SolicitadaPorNombre { get; private set; }
    public DateTimeOffset SolicitadaEn { get; private set; }

    public Guid? DecididaPorId { get; private set; }
    public string? DecididaPorNombre { get; private set; }
    public DateTimeOffset? DecididaEn { get; private set; }
    public string? MotivoDecision { get; private set; }

    // Plazo (antes/después).
    public int? PlazoAnteriorDias { get; private set; }
    public int? PlazoNuevoDias { get; private set; }
    public DateTimeOffset? VencimientoAnterior { get; private set; }
    public DateTimeOffset? VencimientoNuevo { get; private set; }

    // Reversa de movimiento.
    public Guid? MovimientoCarteraId { get; private set; }
    public TipoMovimientoCartera? MovimientoTipo { get; private set; }
    public decimal? MovimientoImporte { get; private set; }

    private CorreccionCartera() { }

    private CorreccionCartera(Guid empresaId, Guid facturaCarteraId, TipoCorreccionCartera tipo, string motivo,
        Guid usuarioId, string? usuarioNombre, DateTimeOffset ahora) : base(Guid.CreateVersion7())
    {
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("CC_MOTIVO_VACIO", "El motivo de la corrección es obligatorio.");
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("CC_USUARIO_NO_IDENTIFICADO", "No se identificó a quien solicita la corrección.");
        EmpresaId = empresaId;
        FacturaCarteraId = facturaCarteraId;
        Tipo = tipo;
        Motivo = motivo.Trim();
        SolicitadaPorId = usuarioId;
        SolicitadaPorNombre = usuarioNombre;
        SolicitadaEn = ahora;
    }

    /// <summary>Plazo corregido: ya aplicado a la factura al registrarse.</summary>
    public static CorreccionCartera RegistrarPlazo(Guid empresaId, FacturaCartera factura, int plazoNuevoDias, string motivo,
        Guid usuarioId, string? usuarioNombre, DateTimeOffset ahora)
    {
        var plazoAnterior = factura.PlazoDias;
        var vencimientoAnterior = factura.CorregirPlazo(plazoNuevoDias);
        return new CorreccionCartera(empresaId, factura.Id, TipoCorreccionCartera.Plazo, motivo, usuarioId, usuarioNombre, ahora)
        {
            Estado = EstadoCorreccionCartera.Aplicada,
            PlazoAnteriorDias = plazoAnterior,
            PlazoNuevoDias = plazoNuevoDias,
            VencimientoAnterior = vencimientoAnterior,
            VencimientoNuevo = factura.FechaVencimiento,
            DecididaPorId = usuarioId,
            DecididaPorNombre = usuarioNombre,
            DecididaEn = ahora,
        };
    }

    public static CorreccionCartera SolicitarReversa(Guid empresaId, MovimientoCartera movimiento, string motivo,
        Guid usuarioId, string? usuarioNombre, DateTimeOffset ahora)
    {
        if (movimiento.Revertido)
            throw new BusinessRuleException("CC_MOVIMIENTO_YA_REVERTIDO", "El movimiento ya fue revertido.");
        if (movimiento.Tipo == TipoMovimientoCartera.ReversaAjusteCobro)
            throw new BusinessRuleException("CC_MOVIMIENTO_NO_REVERSIBLE",
                "La reversa de un ajuste de cobro tiene su propio flujo (revertir ajuste).");
        return new CorreccionCartera(empresaId, movimiento.FacturaCarteraId, TipoCorreccionCartera.ReversaMovimiento, motivo,
            usuarioId, usuarioNombre, ahora)
        {
            Estado = EstadoCorreccionCartera.Pendiente,
            MovimientoCarteraId = movimiento.Id,
            MovimientoTipo = movimiento.Tipo,
            MovimientoImporte = movimiento.Importe,
        };
    }

    /// <summary>Quien solicita no decide su propia solicitud.</summary>
    public void Decidir(bool autorizar, Guid usuarioId, string? usuarioNombre, string? motivoDecision, DateTimeOffset ahora)
    {
        if (Estado != EstadoCorreccionCartera.Pendiente)
            throw new BusinessRuleException("CC_YA_DECIDIDA", "La corrección ya fue decidida.");
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("CC_USUARIO_NO_IDENTIFICADO", "No se identificó a quien decide la corrección.");
        if (usuarioId == SolicitadaPorId)
            throw new BusinessRuleException("CC_MISMA_PERSONA", "Quien solicita la corrección no puede autorizarla ni rechazarla.");
        if (!autorizar && string.IsNullOrWhiteSpace(motivoDecision))
            throw new BusinessRuleException("CC_MOTIVO_RECHAZO_VACIO", "El motivo del rechazo es obligatorio.");

        Estado = autorizar ? EstadoCorreccionCartera.Aplicada : EstadoCorreccionCartera.Rechazada;
        DecididaPorId = usuarioId;
        DecididaPorNombre = usuarioNombre;
        DecididaEn = ahora;
        MotivoDecision = string.IsNullOrWhiteSpace(motivoDecision) ? null : motivoDecision.Trim();
    }
}
