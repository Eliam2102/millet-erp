using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Entidad hija del agregado <see cref="OrdenCompra"/> (diseño §4.10).
/// Modela una firma de autorización (N1 / N2) o un rechazo.
///
/// Invariantes:
/// <list type="bullet">
///   <item>Una <c>(OrdenCompra, Nivel, Resultado=Autorizado)</c> solo
///         puede tener una autorización exitosa (UNIQUE parcial en BD).
///         Rechazos múltiples permitidos.</item>
///   <item><see cref="ResultadoAutorizacionOc.Rechazado"/> requiere
///         <see cref="MotivoRechazoId"/> no null (CHECK BD
///         <c>ck_oc_autorizaciones_motivo</c>).</item>
///   <item><see cref="NivelAutorizacion.Nivel2"/> requiere que ya
///         exista <see cref="NivelAutorizacion.Nivel1"/> autorizada
///         previa (validado por el agregado raíz).</item>
/// </list>
///
/// Implementa <see cref="IAuditable"/> + <see cref="IBelongsToAggregate"/>:
/// firmas y rechazos se registran en <c>core.audit_log</c> agrupados con
/// la OC raíz.
/// </summary>
public sealed class AutorizacionOC : BaseEntity, IAuditable, IBelongsToAggregate
{
    public Guid OrdenCompraId { get; private set; }

    /// <summary>
    /// B.2: <c>AuditSaveChangesInterceptor</c> agrupa la firma con la
    /// OC raíz en <c>core.audit_log</c>.
    /// </summary>
    public Guid AggregateRootId => OrdenCompraId;

    public NivelAutorizacion Nivel { get; private set; }

    public ResultadoAutorizacionOc Resultado { get; private set; }

    public Guid UsuarioId { get; private set; }

    public DateTimeOffset FechaHora { get; private set; }

    /// <summary>FK a <c>compras.motivos_rechazo</c>. Requerido si Resultado=Rechazado.</summary>
    public Guid? MotivoRechazoId { get; private set; }

    public string? MotivoRechazoTexto { get; private set; }

    public string? Notas { get; private set; }

    /// <summary>Constructor para EF Core.</summary>
    private AutorizacionOC() { }

    /// <summary>
    /// Constructor para autorización exitosa (F3-PR1). Invocable solo
    /// desde el agregado raíz (<see cref="OrdenCompra.Autorizar"/>).
    /// </summary>
    internal AutorizacionOC(
        Guid id,
        Guid ordenCompraId,
        NivelAutorizacion nivel,
        Guid usuarioId,
        DateTimeOffset fechaHora,
        string? notas = null) : base(id)
    {
        ValidarComunes(ordenCompraId, usuarioId, notas);
        OrdenCompraId = ordenCompraId;
        Nivel = nivel;
        Resultado = ResultadoAutorizacionOc.Autorizado;
        UsuarioId = usuarioId;
        FechaHora = fechaHora;
        Notas = notas;
    }

    /// <summary>
    /// Constructor para rechazo (F3-PR2). Invocable solo desde el
    /// agregado raíz (<see cref="OrdenCompra.Rechazar"/>).
    /// </summary>
    internal AutorizacionOC(
        Guid id,
        Guid ordenCompraId,
        NivelAutorizacion nivel,
        Guid usuarioId,
        DateTimeOffset fechaHora,
        Guid motivoRechazoId,
        string? motivoRechazoTexto,
        string? notas) : base(id)
    {
        ValidarComunes(ordenCompraId, usuarioId, notas);
        if (motivoRechazoId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "AUTORIZACION_OC_MOTIVO_REQUERIDO",
                "Un rechazo requiere motivoRechazoId no vacío.");
        }
        if (motivoRechazoTexto is { Length: > 500 })
        {
            throw new BusinessRuleException(
                "AUTORIZACION_OC_MOTIVO_TEXTO_LARGO",
                "El texto del motivo no puede exceder 500 caracteres.");
        }
        OrdenCompraId = ordenCompraId;
        Nivel = nivel;
        Resultado = ResultadoAutorizacionOc.Rechazado;
        UsuarioId = usuarioId;
        FechaHora = fechaHora;
        MotivoRechazoId = motivoRechazoId;
        MotivoRechazoTexto = motivoRechazoTexto;
        Notas = notas;
    }

    private static void ValidarComunes(Guid ocId, Guid usuarioId, string? notas)
    {
        if (ocId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "AUTORIZACION_OC_ID_VACIO",
                "La autorización debe pertenecer a una OC.");
        }
        if (usuarioId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "AUTORIZACION_OC_USUARIO_VACIO",
                "La autorización requiere un usuario autorizador.");
        }
        if (notas is { Length: > 500 })
        {
            throw new BusinessRuleException(
                "AUTORIZACION_OC_NOTAS_LARGAS",
                "Las notas no pueden exceder 500 caracteres.");
        }
    }
}
