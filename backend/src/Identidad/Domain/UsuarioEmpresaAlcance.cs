using Millet.SharedKernel.Domain;

namespace Millet.Identidad.Domain;

/// <summary>Alcance empresarial independiente del rol y de la adscripción laboral.</summary>
public sealed class UsuarioEmpresaAlcance : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid UsuarioId { get; private set; }
    public Guid EmpresaId { get; set; }
    public bool EsCorporativo { get; private set; }
    private UsuarioEmpresaAlcance() { }
    public UsuarioEmpresaAlcance(Guid id, Guid usuarioId, Guid empresaId, bool esCorporativo) : base(id)
    {
        if (usuarioId == Guid.Empty || empresaId == Guid.Empty) throw new ArgumentException("Usuario y empresa son obligatorios.");
        UsuarioId = usuarioId;
        EmpresaId = empresaId;
        EsCorporativo = esCorporativo;
    }
    public void Cambiar(bool esCorporativo) => EsCorporativo = esCorporativo;
}
