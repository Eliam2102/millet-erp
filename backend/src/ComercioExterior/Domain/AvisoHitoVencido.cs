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
    /// <summary>M-373: fecha en que el atraso dejó de existir porque se corrigió el hito; null = vigente.</summary>
    public DateOnly? CerradoEn { get; private set; }
    public string? MotivoCierre { get; private set; }
    public bool Vigente => CerradoEn is null;
    private AvisoHitoVencido() { }
    internal AvisoHitoVencido(HitoExpediente hito, DateOnly hoy) : base(Guid.CreateVersion7())
    {
        EmpresaId = hito.EmpresaId;
        ExpedienteId = hito.ExpedienteId;
        HitoId = hito.Id;
        FechaCompromiso = hito.FechaCompromiso!.Value;
        FechaAviso = hoy;
    }

    internal void Cerrar(DateOnly hoy, string motivo)
    {
        if (!Vigente) return;
        CerradoEn = hoy;
        MotivoCierre = motivo;
    }

    internal void Reabrir(DateOnly hoy)
    {
        if (Vigente) return;
        CerradoEn = null;
        MotivoCierre = null;
        FechaAviso = hoy;
    }
}
