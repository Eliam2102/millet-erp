using Millet.SharedKernel.Domain;
namespace Millet.ActivosFijos.Domain;
public sealed class ImpresionEtiquetaActivo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid ActivoId { get; private set; }
    public DateTimeOffset Fecha { get; private set; }
    public Guid ImpresoPor { get; private set; }
    public string Motivo { get; private set; } = "";
    private ImpresionEtiquetaActivo() { }
    internal ImpresionEtiquetaActivo(Activo a, DateTimeOffset fecha, Guid actor, string motivo) : base(Guid.CreateVersion7())
    { EmpresaId=a.EmpresaId; ActivoId=a.Id; Fecha=fecha; ImpresoPor=actor; Motivo=motivo; }
}
public sealed class CapturaAnteriorConteo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid PartidaId { get; private set; }
    public ResultadoConteo Resultado { get; private set; }
    public string? UbicacionReal { get; private set; }
    public string? Evidencia { get; private set; }
    public Guid? CapturadoPor { get; private set; }
    public DateTimeOffset? CapturadoEn { get; private set; }
    public string? MotivoRecaptura { get; private set; }
    private CapturaAnteriorConteo() { }
    internal CapturaAnteriorConteo(PartidaConteoActivo p) : base(Guid.CreateVersion7())
    { EmpresaId=p.EmpresaId; PartidaId=p.Id; Resultado=p.Resultado; UbicacionReal=p.UbicacionReal; Evidencia=p.Evidencia; CapturadoPor=p.CapturadoPor; CapturadoEn=p.CapturadoEn; MotivoRecaptura=p.MotivoRecaptura; }
}
