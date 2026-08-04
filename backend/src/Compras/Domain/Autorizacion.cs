using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain;

/// <summary>
/// Entidad hija del agregado <see cref="Requisicion"/>. Representa una
/// firma de autorización (Nivel1 o Nivel2). Diseño §4.4.
///
/// Invariantes:
/// <list type="bullet">
///   <item>Una <c>(Requisicion, Nivel)</c> solo puede tener una autorización
///         (UNIQUE en BD).</item>
///   <item><c>Nivel2</c> requiere que ya exista <c>Nivel1</c> previo (validado
///         por el agregado raíz al registrar).</item>
/// </list>
///
/// Sin propiedad <c>Tipo</c> (Inicial/Saldo) — los saldos no surtidos son
/// informativos (A12); la autorización inicial cubre todo el monto.
///
/// Implementa <see cref="IAuditable"/> (cuidado §6.2 [P0]).
/// </summary>
public sealed class Autorizacion : BaseEntity, IAuditable, IBelongsToAggregate
{
    public Guid RequisicionId { get; private set; }

    /// <summary>
    /// B.2: para que <c>AuditSaveChangesInterceptor</c> agrupe esta firma
    /// con la <c>Requisicion</c> raíz en <c>core.audit_log</c>.
    /// </summary>
    public Guid AggregateRootId => RequisicionId;

    public NivelAutorizacion Nivel { get; private set; }

    public Guid UsuarioId { get; private set; }

    public DateTimeOffset FechaHora { get; private set; }

    public string? Notas { get; private set; }

    private Autorizacion() { }

    /// <summary>
    /// Constructor <c>internal</c>: solo invocable desde
    /// <see cref="Requisicion.RegistrarAutorizacion"/>.
    /// </summary>
    internal Autorizacion(
        Guid id,
        Guid requisicionId,
        NivelAutorizacion nivel,
        Guid usuarioId,
        DateTimeOffset fechaHora,
        string? notas = null) : base(id)
    {
        if (requisicionId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "AUTORIZACION_REQUISICION_VACIA",
                "La autorización debe pertenecer a una requisición.");
        }

        if (usuarioId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "AUTORIZACION_USUARIO_VACIO",
                "La autorización requiere un usuario autorizador.");
        }

        if (notas is { Length: > 500 })
        {
            throw new BusinessRuleException(
                "AUTORIZACION_NOTAS_DEMASIADO_LARGAS",
                "Las notas no pueden exceder 500 caracteres.");
        }

        RequisicionId = requisicionId;
        Nivel = nivel;
        UsuarioId = usuarioId;
        FechaHora = fechaHora;
        Notas = notas;
    }
}
