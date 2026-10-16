using Millet.SharedKernel.Domain;

namespace Millet.ComercioExterior.Domain;

public sealed class AvisoHitoVencido : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public Guid HitoId { get; private set; }
    public DateOnly FechaCompromiso { get; private set; }
    public DateOnly FechaAviso { get; private set; }
    private AvisoHitoVencido() { }
    internal AvisoHitoVencido(HitoExpediente hito, DateOnly hoy) : base(Guid.CreateVersion7())
    {
        EmpresaId = hito.EmpresaId;
        ExpedienteId = hito.ExpedienteId;
        HitoId = hito.Id;
        FechaCompromiso = hito.FechaCompromiso!.Value;
        FechaAviso = hoy;
    }
}
