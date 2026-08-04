using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Identidad.Domain;

/// <summary>
/// Asociación M:N entre un <see cref="Rol"/> y un grupo de Microsoft
/// Entra ID. Cuando un usuario miembro del grupo entra al sistema, el
/// <c>LoginOrchestrator</c> materializa <see cref="UsuarioEmpresaRol"/>
/// automáticamente (F-Admin-PR3, A3=a mapeo manual por grupo).
///
/// <para>
/// El <see cref="ObjectId"/> es el GUID del grupo en Entra ID (formato
/// string para tolerar tokens fake en dev). <see cref="Nombre"/> es el
/// display name del grupo guardado para mostrar en UI sin ir a Graph en
/// cada render.
/// </para>
///
/// Unique index en (RolId, ObjectId) garantiza unicidad. Ver ADR-0034 §6.4.
/// </summary>
public sealed class RolGrupoEntraId : BaseEntity, IAuditable
{
    public Guid RolId { get; private set; }
    public string ObjectId { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;

    private RolGrupoEntraId() { } // EF Core

    public RolGrupoEntraId(Guid id, Guid rolId, string objectId, string nombre) : base(id)
    {
        if (id == Guid.Empty)
            throw new BusinessRuleException("ROL_GRUPO_ENTRAID_ID_INVALIDO", "El id es obligatorio.");
        if (rolId == Guid.Empty)
            throw new BusinessRuleException("ROL_GRUPO_ENTRAID_ROL_INVALIDO", "RolId es obligatorio.");
        ValidarObjectId(objectId);
        ValidarNombre(nombre);

        RolId = rolId;
        ObjectId = objectId;
        Nombre = nombre;
    }

    /// <summary>
    /// Actualiza solo el display name del grupo (cuando cambia en Entra
    /// ID o se renombra). El <see cref="ObjectId"/> es inmutable —
    /// para cambiarlo se desasocia y re-asocia.
    /// </summary>
    public void ActualizarNombre(string nombre)
    {
        ValidarNombre(nombre);
        Nombre = nombre;
    }

    private static void ValidarObjectId(string objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId) || objectId.Length > 100)
            throw new BusinessRuleException("ROL_GRUPO_ENTRAID_OBJECT_ID_INVALIDO",
                "El ObjectId es requerido y no puede exceder 100 caracteres.");
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("ROL_GRUPO_ENTRAID_NOMBRE_INVALIDO",
                "El nombre del grupo es requerido y no puede exceder 254 caracteres.");
    }
}
