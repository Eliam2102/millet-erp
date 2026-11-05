using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.ProductoTerminado.Domain;

/// <summary>
/// Confirmación de entrega de un albarán (PT5.6): quién recibió, cuándo, con firma o foto como evidencia, lo rechazado en sitio y,
/// si el chofer cobró (contra entrega), el voucher que luego se concilia al liquidar en caja. Una por albarán.
/// </summary>
public sealed class EntregaPt : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int MaxFirmaBytes = 300_000;
    public const int MaxFotoBytes = 2_000_000;

    public Guid EmpresaId { get; set; }
    public Guid SalidaPtId { get; private set; }
    public Guid AlbaranPtId { get; private set; }
    public Guid PedidoPtId { get; private set; }
    public ResultadoEntrega Resultado { get; private set; }
    public string Receptor { get; private set; } = "";
    public int Entregadas { get; private set; }
    public int Rechazadas { get; private set; }
    public string? MotivoRechazo { get; private set; }
    public byte[]? Firma { get; private set; }
    public byte[]? Foto { get; private set; }
    public decimal? CobroMonto { get; private set; }
    public string? CobroForma { get; private set; }
    public string? CobroReferencia { get; private set; }
    /// <summary>Se llena cuando la caja liquida la ruta y el cobro coincide con el voucher (CA5.6).</summary>
    public DateTimeOffset? CobroConciliadoAt { get; private set; }
    public Guid? CobroMostradorId { get; private set; }
    public DateTimeOffset EntregadaAt { get; private set; }
    public string Usuario { get; private set; } = "";

    private EntregaPt() { }

    public static EntregaPt Crear(Guid empresa, Guid salidaId, AlbaranPt albaran, int entregadas, int rechazadas, string? motivo, string receptor, byte[]? firma, byte[]? foto,
        decimal? cobroMonto, string? cobroForma, string? cobroReferencia, DateTimeOffset ahora, string usuario)
    {
        if (firma is null && foto is null)
            throw new BusinessRuleException("PT_ENTREGA_SIN_EVIDENCIA", "La entrega necesita la firma de quien recibe o una foto como evidencia.");
        if (string.IsNullOrWhiteSpace(receptor))
            throw new BusinessRuleException("PT_ENTREGA_SIN_RECEPTOR", "Indica quién recibe.");
        if (rechazadas > 0 && string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("PT_ENTREGA_SIN_MOTIVO", "El rechazo en sitio lleva el motivo.");
        return new EntregaPt(Guid.CreateVersion7())
        {
            EmpresaId = empresa, SalidaPtId = salidaId, AlbaranPtId = albaran.Id, PedidoPtId = albaran.PedidoPtId,
            Resultado = entregadas == 0 ? ResultadoEntrega.Rechazada : rechazadas == 0 ? ResultadoEntrega.Entregada : ResultadoEntrega.Parcial,
            Receptor = receptor.Trim(), Entregadas = entregadas, Rechazadas = rechazadas, MotivoRechazo = rechazadas > 0 ? motivo!.Trim() : null,
            Firma = firma, Foto = foto, CobroMonto = cobroMonto, CobroForma = cobroForma?.Trim(), CobroReferencia = cobroReferencia?.Trim(), EntregadaAt = ahora, Usuario = usuario,
        };
    }

    private EntregaPt(Guid id) : base(id) { }

    /// <summary>Devuelve false si ya estaba conciliado o no hay cobro que conciliar.</summary>
    public bool ConciliarCobro(Guid cobroMostradorId, DateTimeOffset ahora)
    {
        if (CobroMonto is null || CobroConciliadoAt is not null) return false;
        (CobroMostradorId, CobroConciliadoAt) = (cobroMostradorId, ahora);
        return true;
    }
}
