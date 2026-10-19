using Millet.CuentasPorPagar.Domain.Notificaciones;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.FacturaProveedor;

/// <summary>
/// Un registro por aviso de plazo (SLA) emitido para una factura en revisión.
/// Único por (factura, nivel, día) — P8.4a A2: el worker no avisa dos veces el
/// mismo día. La bandeja por área muestra el nivel más alto del episodio
/// vigente (misma <see cref="FechaEntradaRevision"/>).
/// </summary>
public sealed class AvisoSlaRevision : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public Guid FacturaProveedorId { get; private set; }
    public Guid DependenciaRevisoraId { get; private set; }
    public Guid MotivoRevisionId { get; private set; }
    public NivelEscalamientoSla Nivel { get; private set; }

    /// <summary>Día local (México) de la evaluación que emitió el aviso.</summary>
    public DateOnly Fecha { get; private set; }

    public int DiasHabiles { get; private set; }
    public int SlaDias { get; private set; }
    public DateTimeOffset FechaEntradaRevision { get; private set; }
    public DateTimeOffset EmitidoEn { get; private set; }

    private AvisoSlaRevision() { }

    public AvisoSlaRevision(
        Guid empresaId,
        Guid facturaProveedorId,
        Guid dependenciaRevisoraId,
        Guid motivoRevisionId,
        NivelEscalamientoSla nivel,
        DateOnly fecha,
        int diasHabiles,
        int slaDias,
        DateTimeOffset fechaEntradaRevision,
        DateTimeOffset emitidoEn) : base(Guid.CreateVersion7())
    {
        if (facturaProveedorId == Guid.Empty)
            throw new BusinessRuleException("AVISO_SLA_FACTURA_VACIA", "La factura del aviso de plazo es obligatoria.");

        EmpresaId = empresaId;
        FacturaProveedorId = facturaProveedorId;
        DependenciaRevisoraId = dependenciaRevisoraId;
        MotivoRevisionId = motivoRevisionId;
        Nivel = nivel;
        Fecha = fecha;
        DiasHabiles = diasHabiles;
        SlaDias = slaDias;
        FechaEntradaRevision = fechaEntradaRevision;
        EmitidoEn = emitidoEn;
    }
}
