using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Corridas;

/// <summary>Firma de autorización de una corrida (T9.1-A). Una por usuario y corrida; guarda sus roles al firmar.</summary>
public sealed class CorridaPagoFirma : BaseEntity, IAuditable
{
    public Guid CorridaId { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string Roles { get; private set; } = string.Empty;
    public DateTimeOffset FirmadaEn { get; private set; }

    private CorridaPagoFirma() { }

    internal CorridaPagoFirma(Guid corridaId, Guid usuarioId, IEnumerable<string> roles, DateTimeOffset ahora) : base(Guid.CreateVersion7())
    {
        CorridaId = corridaId; UsuarioId = usuarioId; Roles = string.Join(',', roles); FirmadaEn = ahora;
    }

    public bool TieneRol(string rol) =>
        Roles.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(rol, StringComparer.OrdinalIgnoreCase);
}
