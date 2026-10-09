namespace Millet.Identidad.Domain;

/// <summary>Pertenencia vigente obtenida de un token validado o Graph en el último inicio de sesión.</summary>
public sealed class UsuarioGrupoEntraId
{
    public Guid UsuarioId { get; private set; }
    public string ObjectId { get; private set; } = string.Empty;
    private UsuarioGrupoEntraId() { }
    public UsuarioGrupoEntraId(Guid usuarioId, string objectId)
    {
        if (usuarioId == Guid.Empty || string.IsNullOrWhiteSpace(objectId) || objectId.Length > 100)
            throw new Millet.SharedKernel.Application.Exceptions.BusinessRuleException("GRUPO_ENTRA_INVALIDO", "El usuario y el identificador de grupo de Microsoft son obligatorios.");
        UsuarioId = usuarioId;
        ObjectId = objectId.Trim().ToLowerInvariant();
    }
}
