using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.FacturaProveedor;

/// <summary>
/// Catálogo unificado de motivos de revisión (§5.3 del 00-levantamiento,
/// §A22 del 01-diseno). Seedea 14 motivos al deploy con
/// <see cref="SlaDias"/> por motivo — SLA único de 5 días excepto
/// "Disputa contractual" (15) y "Indicación expresa" (sin SLA, null).
///
/// <para>
/// El campo <see cref="DependenciaRevisoraDefaultCodigo"/> indica el
/// código de la dependencia revisora que típicamente libera ese
/// motivo. Es informativo para la UI — el usuario puede asignar otra
/// dependencia al enviar a revisión.
/// </para>
/// </summary>
public sealed class MotivoRevision : BaseEntity
{
    public string Codigo { get; private set; } = default!;
    public string Nombre { get; private set; } = default!;
    public string? Descripcion { get; private set; }

    /// <summary>SLA en días hábiles. NULL = sin SLA (motivo "Indicación expresa").</summary>
    public int? SlaDias { get; private set; }

    /// <summary>Código de la dependencia revisora que típicamente libera. Informativo.</summary>
    public string? DependenciaRevisoraDefaultCodigo { get; private set; }

    public bool Activo { get; private set; }

    private MotivoRevision() { }

    public MotivoRevision(
        Guid id,
        string codigo,
        string nombre,
        string? descripcion,
        int? slaDias,
        string? dependenciaRevisoraDefaultCodigo,
        bool activo = true) : base(id)
    {
        Codigo = codigo;
        Nombre = nombre;
        Descripcion = descripcion;
        SlaDias = slaDias;
        DependenciaRevisoraDefaultCodigo = dependenciaRevisoraDefaultCodigo;
        Activo = activo;
    }
}
