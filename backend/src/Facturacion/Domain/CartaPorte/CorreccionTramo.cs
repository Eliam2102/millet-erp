using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.CartaPorte;

/// <summary>Un campo de la Carta Porte cambiado en una corrección (valor anterior y nuevo, en texto).</summary>
public sealed record CambioTramo(string Campo, string? Anterior, string? Nuevo);

/// <summary>
/// M-354: renglón de la bitácora de correcciones de una Carta Porte observada (timbrado fallido): qué campo cambió, de qué a
/// qué, por qué, quién y cuándo. Inmutable (renglón de bitácora, <see cref="INotAudited"/>).
/// </summary>
public sealed class CorreccionTramo : BaseEntity, IPerteneceAEmpresa, INotAudited
{
    public Guid EmpresaId { get; set; }
    public Guid CartaPorteId { get; private set; }
    public Guid? TramoTrasladoId { get; private set; }
    public string Campo { get; private set; } = string.Empty;
    public string? ValorAnterior { get; private set; }
    public string? ValorNuevo { get; private set; }
    public string Motivo { get; private set; } = string.Empty;
    public Guid? UsuarioId { get; private set; }
    public string? UsuarioNombre { get; private set; }
    public DateTimeOffset En { get; private set; }

    private CorreccionTramo() { }

    public CorreccionTramo(
        Guid empresaId, Guid cartaPorteId, Guid? tramoId, CambioTramo cambio, string motivo, Guid? usuarioId,
        string? usuarioNombre, DateTimeOffset en) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresaId;
        CartaPorteId = cartaPorteId;
        TramoTrasladoId = tramoId;
        Campo = cambio.Campo;
        ValorAnterior = cambio.Anterior;
        ValorNuevo = cambio.Nuevo;
        Motivo = motivo;
        UsuarioId = usuarioId;
        UsuarioNombre = usuarioNombre;
        En = en;
    }
}
