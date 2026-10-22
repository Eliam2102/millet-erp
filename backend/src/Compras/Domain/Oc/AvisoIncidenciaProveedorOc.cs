using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Aviso de compromiso vencido de una incidencia del proveedor (F1-COM-07, C3.5-b/d). Único por
/// (incidencia, fecha compromiso): la revisión diaria no repite el aviso; una fecha reprogramada
/// que vuelve a vencer genera otro.
/// </summary>
public sealed class AvisoIncidenciaProveedorOc : BaseEntity, INotAudited
{
    public Guid IncidenciaProveedorOcId { get; private set; }
    public Guid OrdenCompraId { get; private set; }
    public DateOnly FechaCompromiso { get; private set; }
    public Guid ResponsableId { get; private set; }
    public DateTimeOffset EnviadoEn { get; private set; }

    private AvisoIncidenciaProveedorOc() { }

    public AvisoIncidenciaProveedorOc(Guid incidenciaId, Guid ordenCompraId, DateOnly fechaCompromiso,
        Guid responsableId, DateTimeOffset enviadoEn) : base(Guid.CreateVersion7())
    {
        IncidenciaProveedorOcId = incidenciaId;
        OrdenCompraId = ordenCompraId;
        FechaCompromiso = fechaCompromiso;
        ResponsableId = responsableId;
        EnviadoEn = enviadoEn;
    }
}
