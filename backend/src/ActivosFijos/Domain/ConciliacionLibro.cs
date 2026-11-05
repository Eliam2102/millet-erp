using Millet.SharedKernel.Domain;
namespace Millet.ActivosFijos.Domain;
public sealed class ConciliacionLibro : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public DateOnly Periodo { get; private set; }
    public Guid? SucursalId { get; private set; }
    public string ReporteJson { get; private set; } = "";
    public string ClasificacionesJson { get; private set; } = "";
    public string Huella { get; private set; } = "";
    public Guid AceptadaPor { get; private set; }
    public DateTimeOffset AceptadaEn { get; private set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string Estado { get; set; } = "Aceptada";
    private ConciliacionLibro() { }
    public ConciliacionLibro(Guid empresa, DateOnly periodo, Guid? sucursal, string reporte, string clasificaciones, string huella, Guid actor, DateTimeOffset ahora) : base(Guid.CreateVersion7())
    { EmpresaId=empresa; Periodo=CalculoDepreciacion.Mes(periodo); SucursalId=sucursal; ReporteJson=reporte; ClasificacionesJson=clasificaciones; Huella=huella; AceptadaPor=actor; AceptadaEn=ahora; }
}
