using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.ProductoTerminado.Domain;

/// <summary>
/// M-245: salida de un pedido que no tiene todas sus piezas en PT. Se pide con motivo y las piezas que salen; la autoriza Crédito y
/// Cobranza (en su ausencia Gerencia de Ventas, con motivo de suplencia; Guadalupe 9-nov), siempre otra persona distinta de quien la pide.
/// Autorizada, el pedido entra a 69/70 y solo esas piezas se pueden cargar; el resto queda como remanente del mismo pedido.
/// </summary>
public sealed class SolicitudSalidaParcialPt : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid PedidoPtId { get; private set; }
    public List<Guid> PiezaIds { get; private set; } = [];
    public string Motivo { get; private set; } = "";
    public EstadoSolicitudPt Estado { get; private set; }
    public Guid SolicitadaPorId { get; private set; }
    public DateTimeOffset SolicitadaAt { get; private set; }
    public string? Nivel { get; private set; }
    public string? Suplencia { get; private set; }
    public string? MotivoResolucion { get; private set; }
    public Guid? ResueltaPorId { get; private set; }
    public DateTimeOffset? ResueltaAt { get; private set; }

    private SolicitudSalidaParcialPt() { }

    public static SolicitudSalidaParcialPt Crear(Guid empresa, Guid pedidoPtId, IReadOnlyCollection<Guid> piezaIds, string motivo, Guid actor, DateTimeOffset ahora)
    {
        if (piezaIds.Count == 0 || piezaIds.Distinct().Count() != piezaIds.Count)
            throw new BusinessRuleException("PT_PARCIAL_PIEZAS_INVALIDAS", "Indique las piezas que salen, sin repetir.");
        return new SolicitudSalidaParcialPt
        {
            Id = Guid.CreateVersion7(), EmpresaId = empresa, PedidoPtId = pedidoPtId, PiezaIds = [.. piezaIds], Motivo = ReglasSalidaPt.MotivoValido(motivo),
            Estado = EstadoSolicitudPt.Pendiente, SolicitadaPorId = actor, SolicitadaAt = ahora,
        };
    }

    /// <summary><paramref name="nivel"/>: credito o ventas (suplente, con <paramref name="suplencia"/>).</summary>
    public void Autorizar(string nivel, string? suplencia, string motivo, Guid actor, DateTimeOffset ahora) => Resolver(EstadoSolicitudPt.Autorizada, nivel, suplencia, motivo, actor, ahora);

    public void Rechazar(string motivo, Guid actor, DateTimeOffset ahora) => Resolver(EstadoSolicitudPt.Rechazada, null, null, motivo, actor, ahora);

    private void Resolver(EstadoSolicitudPt estado, string? nivel, string? suplencia, string motivo, Guid actor, DateTimeOffset ahora)
    {
        if (Estado != EstadoSolicitudPt.Pendiente) throw new BusinessRuleException("PT_SOLICITUD_YA_RESUELTA", "La solicitud ya se resolvió.");
        if (actor == SolicitadaPorId) throw new BusinessRuleException("PT_SALIDA_SEPARACION_FUNCIONES", "Quien pide la salida parcial no puede autorizarla ni rechazarla.");
        (Estado, Nivel, Suplencia, MotivoResolucion, ResueltaPorId, ResueltaAt) = (estado, nivel, suplencia, ReglasSalidaPt.MotivoValido(motivo), actor, ahora);
    }
}
